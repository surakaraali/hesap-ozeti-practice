using System.Net;
using HesapOzeti.Api.Data;
using HesapOzeti.Api.Sync;
using Microsoft.Extensions.Logging.Abstractions;

namespace HesapOzeti.Tests;

public class BankApiClientTests
{
    [Fact]
    public async Task Maps_a_v1_transaction_to_our_model()
    {
        var bank = new StubBank(_ => """
            {"accountIban":"TR01","transactions":[
              {"refNo":"R1","txnDate":"05.11.2026 14:30:00","amount":"1250.50","currency":"TRY","explanation":"EFT GELEN","type":"A"}]}
            """);
        var client = new BankApiClient(new HttpClient(bank) { BaseAddress = new Uri("http://bank/") }, NullLogger<BankApiClient>.Instance);

        var result = await client.GetTransactionsAsync("TR01", new DateOnly(2026, 11, 5));

        var t = Assert.Single(result);
        Assert.Equal(new BankTransactionDto("R1", new DateTime(2026, 11, 5, 14, 30, 0), 1250.50m, "TRY", "EFT GELEN", Direction.Incoming), t);
        Assert.Contains(bank.Requests, r => r.Contains("date=05.11.2026"));
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
