using System.Net;
using HesapOzeti.Api.Data;
using HesapOzeti.Api.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HesapOzeti.Tests;

public class TransactionSyncServiceTests
{
    static readonly DateOnly Today = new(2026, 11, 5);

    [Fact]
    public async Task One_failing_account_does_not_stop_the_others()
    {
        await using var db = TestDb.NewContext();
        var broken = await TestDb.NewAccountAsync(db);
        var healthy = await TestDb.NewAccountAsync(db);
        var bank = new StubBank(r =>
            r.RequestUri!.Query.Contains(broken.Iban) ? (HttpStatusCode.ServiceUnavailable, "{}")
            : r.RequestUri.Query.Contains(healthy.Iban) ? (HttpStatusCode.OK, ItemJson(healthy.Iban, r.RequestUri.Query))
            : (HttpStatusCode.OK, """{"iban":"x","items":[]}"""));

        var result = await Service(db, bank).SyncAsync(Today);

        Assert.True(result.FailedAccounts >= 1);
        Assert.Equal(2, await db.BankTransactions.CountAsync(t => t.BankAccountId == healthy.Id)); // yesterday + today
        Assert.Equal(0, await db.BankTransactions.CountAsync(t => t.BankAccountId == broken.Id));
    }

    [Fact]
    public async Task Picks_up_yesterdays_transactions_and_does_not_save_them_twice()
    {
        await using var db = TestDb.NewContext();
        var account = await TestDb.NewAccountAsync(db);
        var bank = new StubBank(r => r.RequestUri!.Query.Contains(account.Iban)
            ? (HttpStatusCode.OK, ItemJson(account.Iban, r.RequestUri.Query))
            : (HttpStatusCode.OK, """{"iban":"x","items":[]}"""));
        var service = Service(db, bank);

        await service.SyncAsync(Today);
        await service.SyncAsync(Today);

        var dates = await db.BankTransactions.Where(t => t.BankAccountId == account.Id).Select(t => t.TransactionDate.Date).ToListAsync();
        Assert.Equal([new DateTime(2026, 11, 4), new DateTime(2026, 11, 5)], dates.Order());
    }

    static TransactionSyncService Service(AppDbContext db, StubBank bank) =>
        new(db, BankApiClientTests.Client("v2", bank).Item1, NullLogger<TransactionSyncService>.Instance);

    /// <summary>One transaction per IBAN and day, dated on the day that was asked for.</summary>
    static string ItemJson(string iban, string query)
    {
        var date = query[(query.IndexOf("date=") + 5)..];
        return $$"""
            {"iban":"{{iban}}","items":[{"referenceNumber":"{{iban[^6..]}}{{date}}","valueDate":"{{date}}T10:00:00+03:00",
             "amount":10,"currencyCode":"TRY","description":"t","direction":"CREDIT"}]}
            """;
    }
}
