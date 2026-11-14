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

    static (BankApiClient, StubBank) Client(string? version, string json)
    {
        var bank = new StubBank(_ => json);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Bank:ApiVersion"] = version }).Build();
        return (new BankApiClient(new HttpClient(bank) { BaseAddress = new Uri("http://bank/") }, config, NullLogger<BankApiClient>.Instance), bank);
    }
}

/// <summary>Answers the token call itself and every other call with the given function.</summary>
public class StubBank(Func<HttpRequestMessage, string> respond, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request.RequestUri!.PathAndQuery);
        var isToken = request.RequestUri.AbsolutePath.EndsWith("/oauth/token");
        return Task.FromResult(new HttpResponseMessage(isToken ? HttpStatusCode.OK : status)
        {
            Content = new StringContent(isToken ? """{"accessToken":"t","expiresIn":120}""" : respond(request),
                System.Text.Encoding.UTF8, "application/json"),
        });
    }
}
