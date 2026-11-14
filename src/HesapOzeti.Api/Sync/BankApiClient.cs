using System.Globalization;
using System.Net.Http.Headers;
using HesapOzeti.Api.Data;
using HesapOzeti.Api.FakeBank;

namespace HesapOzeti.Api.Sync;

/// <summary>Our transaction model, independent of the bank's API version.</summary>
public record BankTransactionDto(string ReferenceNo, DateTime Date, decimal Amount, string Currency, string Description, Direction Direction);

/// <summary>Adapter for the bank API (v1).</summary>
public class BankApiClient(HttpClient http, ILogger<BankApiClient> log)
{
    static string? _token;

    public async Task<List<BankTransactionDto>> GetTransactionsAsync(string iban, DateOnly date, CancellationToken ct = default)
    {
        try
        {
            _token ??= await GetTokenAsync(ct);

            var request = new HttpRequestMessage(HttpMethod.Get, $"fake-bank/v1/transactions?iban={iban}&date={date:dd.MM.yyyy}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return [];

            var body = await response.Content.ReadFromJsonAsync<V1Response>(ct);
            return body!.Transactions.Select(t => new BankTransactionDto(
                t.RefNo,
                DateTime.ParseExact(t.TxnDate, "dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                decimal.Parse(t.Amount, CultureInfo.InvariantCulture),
                t.Currency,
                t.Explanation,
                t.Type == "A" ? Direction.Incoming : Direction.Outgoing)).ToList();
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Bank call failed");
            return [];
        }
    }

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
}
