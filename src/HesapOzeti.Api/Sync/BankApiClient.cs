using System.Globalization;
using System.Net;
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
public class BankApiClient(HttpClient http, IConfiguration config, BankTokenStore tokens, ILogger<BankApiClient> log)
{
    /// <summary>Throws on any bank error: an empty list must mean "no transactions", never "the call failed".</summary>
    public async Task<List<BankTransactionDto>> GetTransactionsAsync(string iban, DateOnly date, CancellationToken ct = default)
    {
        var v1 = config["Bank:ApiVersion"] == "v1";
        var url = v1
            ? $"fake-bank/v1/transactions?iban={iban}&date={date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}"
            : $"fake-bank/v2/transactions?iban={iban}&date={date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

        var response = await SendWithRetryAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // The bank can revoke a token before its expiry. Get a fresh one and try once more.
            log.LogWarning("Bank returned 401 for {Iban}, refreshing the token and retrying once", iban);
            tokens.Clear();
            response = await SendWithRetryAsync(url, ct);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Bank returned {(int)response.StatusCode} for {iban} on {date:yyyy-MM-dd}: {body}", null, response.StatusCode);
        }

        return v1 ? MapV1((await response.Content.ReadFromJsonAsync<V1Response>(ct))!)
                  : MapV2((await response.Content.ReadFromJsonAsync<V2Response>(ct))!);
    }

    static readonly HashSet<HttpStatusCode> Transient =
    [
        HttpStatusCode.RequestTimeout, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway, HttpStatusCode.ServiceUnavailable, HttpStatusCode.GatewayTimeout,
    ];

    /// <summary>
    /// Retries temporary failures (5xx, 408, 429, network errors) with a growing wait: 0.5 s, 1 s, ...
    /// Other errors (400, 401, 404) are returned at once, because trying again would give the same answer.
    /// Reading transactions is safe to repeat, so retrying here can never create anything twice.
    /// </summary>
    async Task<HttpResponseMessage> SendWithRetryAsync(string url, CancellationToken ct)
    {
        var maxAttempts = config.GetValue("Bank:MaxAttempts", 3);
        var baseDelay = TimeSpan.FromMilliseconds(config.GetValue("Bank:RetryBaseDelayMs", 500));

        for (var attempt = 1; ; attempt++)
        {
            string reason;
            try
            {
                var response = await SendAsync(url, ct);
                if (!Transient.Contains(response.StatusCode) || attempt >= maxAttempts) return response;
                reason = $"HTTP {(int)response.StatusCode}";
            }
            catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested && attempt < maxAttempts)
            {
                reason = ex.GetType().Name;
            }

            var delay = baseDelay * Math.Pow(2, attempt - 1);
            log.LogWarning("Bank call failed ({Reason}), attempt {Attempt}/{Max}, retrying in {Delay} ms: {Url}",
                reason, attempt, maxAttempts, delay.TotalMilliseconds, url);
            await Task.Delay(delay, ct);
        }
    }

    async Task<HttpResponseMessage> SendAsync(string url, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(ct));
        return await http.SendAsync(request, ct);
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
        if (tokens.Get() is { } cached) return cached;

        var response = await http.PostAsJsonAsync("fake-bank/oauth/token",
            new FakeBankEndpoints.TokenRequest(FakeBankEndpoints.ClientId, FakeBankEndpoints.ClientSecret), ct);
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>(ct))!;
        tokens.Set(token.AccessToken, token.ExpiresIn);
        log.LogInformation("Got a new bank token, valid for {Seconds} s", token.ExpiresIn);
        return token.AccessToken;
    }

    record TokenResponse(string AccessToken, int ExpiresIn);
    record V1Response(string AccountIban, List<V1Transaction> Transactions);
    record V1Transaction(string RefNo, string TxnDate, string Amount, string Currency, string Explanation, string Type);
    record V2Response(string Iban, List<V2Transaction> Items);
    record V2Transaction(string ReferenceNumber, DateTimeOffset ValueDate, decimal Amount, string CurrencyCode, string Description, string Direction);
}

/// <summary>
/// Keeps the bank token with its expiry time. A singleton, because the typed HttpClient is created anew for every use.
/// The token is renewed 30 seconds early so a call never starts with a token that runs out on the way.
/// </summary>
public class BankTokenStore(TimeProvider clock)
{
    static readonly TimeSpan Margin = TimeSpan.FromSeconds(30);
    readonly Lock _lock = new();
    string? _token;
    DateTimeOffset _expiresAt;

    public string? Get()
    {
        lock (_lock) return _token is not null && clock.GetUtcNow() < _expiresAt - Margin ? _token : null;
    }

    public void Set(string token, int expiresInSeconds)
    {
        lock (_lock) (_token, _expiresAt) = (token, clock.GetUtcNow().AddSeconds(expiresInSeconds));
    }

    public void Clear()
    {
        lock (_lock) _token = null;
    }
}
