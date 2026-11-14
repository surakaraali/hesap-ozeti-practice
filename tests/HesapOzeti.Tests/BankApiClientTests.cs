using System.Net;
using HesapOzeti.Api.Data;
using HesapOzeti.Api.Sync;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HesapOzeti.Tests;

public class BankApiClientTests
{
    const string V1Json = """
        {"accountIban":"TR01","transactions":[
          {"refNo":"R1","txnDate":"05.11.2026 14:30:00","amount":"1250.50","currency":"TRY","explanation":"EFT GELEN","type":"A"}]}
        """;

    const string V2Json = """
        {"iban":"TR01","items":[
          {"referenceNumber":"R1","valueDate":"2026-11-05T14:30:00+03:00","amount":1250.50,"currencyCode":"TRY","description":"EFT GELEN","direction":"CREDIT"}]}
        """;

    static readonly BankTransactionDto Expected = new("R1", new DateTime(2026, 11, 5, 14, 30, 0), 1250.50m, "TRY", "EFT GELEN", Direction.Incoming);

    [Fact]
    public async Task Maps_a_v1_transaction_to_our_model()
    {
        var (client, bank) = Client("v1", V1Json);

        var t = Assert.Single(await client.GetTransactionsAsync("TR01", new DateOnly(2026, 11, 5)));

        Assert.Equal(Expected, t);
        Assert.Contains(bank.Requests, r => r == "/fake-bank/v1/transactions?iban=TR01&date=05.11.2026");
    }

    [Fact]
    public async Task Maps_a_v2_transaction_to_the_same_model()
    {
        var (client, bank) = Client("v2", V2Json);

        var t = Assert.Single(await client.GetTransactionsAsync("TR01", new DateOnly(2026, 11, 5)));

        Assert.Equal(Expected, t);
        Assert.Contains(bank.Requests, r => r == "/fake-bank/v2/transactions?iban=TR01&date=2026-11-05");
    }

    [Fact]
    public async Task Uses_v2_when_no_version_is_configured()
    {
        var (client, bank) = Client(null, V2Json);

        await client.GetTransactionsAsync("TR01", new DateOnly(2026, 11, 5));

        Assert.Contains(bank.Requests, r => r.StartsWith("/fake-bank/v2/"));
    }

    [Fact]
    public async Task Gets_a_new_token_when_the_old_one_is_about_to_expire()
    {
        var clock = new TestClock();
        var (client, bank) = Client("v2", V2Json, new BankTokenStore(clock));

        await client.GetTransactionsAsync("TR01", new DateOnly(2026, 11, 5));
        clock.Advance(TimeSpan.FromSeconds(60));
        await client.GetTransactionsAsync("TR01", new DateOnly(2026, 11, 5)); // 60 s left of 120: still valid
        clock.Advance(TimeSpan.FromSeconds(40));
        await client.GetTransactionsAsync("TR01", new DateOnly(2026, 11, 5)); // 20 s left, inside the 30 s margin

        Assert.Equal(2, bank.TokenRequests);
    }

    [Fact]
    public async Task Refreshes_the_token_and_retries_once_after_a_401()
    {
        var calls = 0;
        var bank = new StubBank(_ => ++calls == 1 ? (HttpStatusCode.Unauthorized, """{"error":"invalid_token"}""") : (HttpStatusCode.OK, V2Json));
        var (client, _) = Client("v2", bank);

        var t = Assert.Single(await client.GetTransactionsAsync("TR01", new DateOnly(2026, 11, 5)));

        Assert.Equal(Expected, t);
        Assert.Equal(2, bank.TokenRequests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)] // still 401 after the refresh
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Throws_instead_of_returning_an_empty_list_when_the_bank_fails(HttpStatusCode status)
    {
        var (client, _) = Client("v2", new StubBank(_ => (status, """{"error":"x"}""")));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetTransactionsAsync("TR01", new DateOnly(2026, 11, 5)));

        Assert.Equal(status, ex.StatusCode);
        Assert.Contains("TR01", ex.Message);
    }

    [Fact]
    public async Task Retries_a_temporary_bank_error_and_then_succeeds()
    {
        var calls = 0;
        var bank = new StubBank(_ => ++calls < 3 ? (HttpStatusCode.ServiceUnavailable, "{}") : (HttpStatusCode.OK, V2Json));
        var (client, _) = Client("v2", bank);

        Assert.Single(await client.GetTransactionsAsync("TR01", new DateOnly(2026, 11, 5)));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Does_not_retry_an_error_that_would_repeat()
    {
        var calls = 0;
        var (client, _) = Client("v2", new StubBank(_ => { calls++; return (HttpStatusCode.BadRequest, "{}"); }));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetTransactionsAsync("TR01", new DateOnly(2026, 11, 5)));
        Assert.Equal(1, calls);
    }

    static (BankApiClient, StubBank) Client(string? version, string json, BankTokenStore? tokens = null) =>
        Client(version, new StubBank(_ => (HttpStatusCode.OK, json)), tokens);

    internal static (BankApiClient, StubBank) Client(string? version, StubBank bank, BankTokenStore? tokens = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Bank:ApiVersion"] = version, ["Bank:RetryBaseDelayMs"] = "0" }).Build();
        var client = new BankApiClient(new HttpClient(bank) { BaseAddress = new Uri("http://bank/") }, config,
            tokens ?? new BankTokenStore(TimeProvider.System), NullLogger<BankApiClient>.Instance);
        return (client, bank);
    }
}

/// <summary>Hands out 120-second tokens itself and answers every other call with the given function.</summary>
public class StubBank(Func<HttpRequestMessage, (HttpStatusCode Status, string Json)> respond) : HttpMessageHandler
{
    public List<string> Requests { get; } = [];
    public int TokenRequests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request.RequestUri!.PathAndQuery);
        var (status, json) = request.RequestUri.AbsolutePath.EndsWith("/oauth/token")
            ? (HttpStatusCode.OK, $$"""{"accessToken":"t{{++TokenRequests}}","expiresIn":120}""")
            : respond(request);
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
    }
}

public class TestClock : TimeProvider
{
    DateTimeOffset _now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}
