using System.Globalization;
using System.Net.Http.Headers;
using HesapOzeti.Api.Data;
using HesapOzeti.Api.FakeBank;

namespace HesapOzeti.Api.Sync;

/// <summary>Our transaction model, independent of the bank's API version.</summary>
public record BankTransactionDto(string ReferenceNo, DateTime Date, decimal Amount, string Currency, string Description, Direction Direction);

/// <summary>
/// Adapter for the bank API. Turns the bank's format into our model, so nothing else changes when the bank
/// changes its API. Bank:ApiVersion picks v2 (default) or v1, so we can switch back without a deploy.
/// </summary>
public class BankApiClient(HttpClient http, IConfiguration config, ILogger<BankApiClient> log)
{
    static string? _token;

    public async Task<List<BankTransactionDto>> GetTransactionsAsync(string iban, DateOnly date, CancellationToken ct = default)
    {
        try
        {
            _token ??= await GetTokenAsync(ct);

            var v1 = config["Bank:ApiVersion"] == "v1";
            var url = v1
                ? $"fake-bank/v1/transactions?iban={iban}&date={date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}"
                : $"fake-bank/v2/transactions?iban={iban}&date={date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return [];

            return v1 ? MapV1((await response.Content.ReadFromJsonAsync<V1Response>(ct))!)
                      : MapV2((await response.Content.ReadFromJsonAsync<V2Response>(ct))!);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Bank call failed");
            return [];
        }
    }

    static List<BankTransactionDto> MapV1(V1Response body) => body.Transactions.Select(t => new BankTransactionDto(
        t.RefNo,
        DateTime.ParseExact(t.TxnDate, "dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture),
        decimal.Parse(t.Amount, CultureInfo.InvariantCulture),
        t.Currency,
        t.Explanation,
        t.Type == "A" ? Direction.Incoming : Direction.Outgoing)).ToList();

    // valueDate carries the bank's offset (+03:00). We store the bank's local time, as v1 did.
    static List<BankTransactionDto> MapV2(V2Response body) => body.Items.Select(t => new BankTransactionDto(
        t.ReferenceNumber,
        t.ValueDate.DateTime,
        t.Amount,
        t.CurrencyCode,
        t.Description,
        t.Direction switch
        {
            "CREDIT" => Direction.Incoming,
            "DEBIT" => Direction.Outgoing,
            _ => throw new FormatException($"Unknown direction '{t.Direction}' for {t.ReferenceNumber}"),
        })).ToList();

    async Task<string> GetTokenAsync(CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync("fake-bank/oauth/token",
            new FakeBankEndpoints.TokenRequest(FakeBankEndpoints.ClientId, FakeBankEndpoints.ClientSecret), ct);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        return token!.AccessToken;
    }

    record TokenResponse(string AccessToken, int ExpiresIn);
    record V1Response(string AccountIban, List<V1Transaction> Transactions);
    record V1Transaction(string RefNo, string TxnDate, string Amount, string Currency, string Explanation, string Type);
    record V2Response(string Iban, List<V2Transaction> Items);
    record V2Transaction(string ReferenceNumber, DateTimeOffset ValueDate, decimal Amount, string CurrencyCode, string Description, string Direction);
}
