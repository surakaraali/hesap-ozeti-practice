using HesapOzeti.Api.Data;
using HesapOzeti.Api.Erp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HesapOzeti.Tests;

public class ErpExportServiceTests
{
    [Fact]
    public async Task Exports_a_pending_transaction_as_one_voucher()
    {
        await using var db = TestDb.NewContext();
        var tx = await PendingTransactionAsync(db);

        await Export(db, timeoutRate: 0);

        Assert.Equal(ErpSyncStatus.Exported, tx.ErpSyncStatus);
        var voucher = await db.ErpVouchers.SingleAsync(v => v.BankTransactionId == tx.Id);
        Assert.Equal(("Tahsilat", 100m), (voucher.VoucherType, voucher.Amount));
    }

    [Fact]
    public async Task A_retry_after_a_Tiger_timeout_does_not_create_a_second_voucher()
    {
        await using var db = TestDb.NewContext();
        var tx = await PendingTransactionAsync(db);

        await Export(db, timeoutRate: 1); // Tiger saves the voucher, but we only see the timeout
        Assert.Equal(ErpSyncStatus.Pending, tx.ErpSyncStatus);

        await Export(db, timeoutRate: 0); // next run sends the same transaction again

        Assert.Equal(ErpSyncStatus.Exported, tx.ErpSyncStatus);
        Assert.Equal(1, await db.ErpVouchers.CountAsync(v => v.BankTransactionId == tx.Id));
    }

    [Fact]
    public async Task The_same_bank_reference_cannot_be_saved_twice_for_an_account()
    {
        await using var db = TestDb.NewContext();
        var tx = await PendingTransactionAsync(db);

        db.BankTransactions.Add(Copy(tx));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("IX_BankTransactions_BankAccountId_BankReferenceNo", ex.InnerException!.Message);
    }

    static async Task Export(AppDbContext db, double timeoutRate)
    {
        var tiger = new FakeTigerClient(TestDb.Factory, new ConfigurationBuilder().Build()) { TimeoutRate = timeoutRate };
        await new ErpExportService(db, tiger, NullLogger<ErpExportService>.Instance).ExportPendingAsync(batchSize: 10_000);
    }

    static async Task<BankTransaction> PendingTransactionAsync(AppDbContext db)
    {
        var account = await TestDb.NewAccountAsync(db);
        var tx = new BankTransaction
        {
            BankAccountId = account.Id, BankReferenceNo = "R-" + Guid.NewGuid().ToString("N")[..8], TransactionDate = DateTime.Today,
            Amount = 100m, Currency = "TRY", Description = "test", Direction = Direction.Incoming, CreatedAt = DateTime.UtcNow,
        };
        db.BankTransactions.Add(tx);
        await db.SaveChangesAsync();
        return tx;
    }

    static BankTransaction Copy(BankTransaction t) => new()
    {
        BankAccountId = t.BankAccountId, BankReferenceNo = t.BankReferenceNo, TransactionDate = t.TransactionDate, Amount = t.Amount,
        Currency = t.Currency, Description = t.Description, Direction = t.Direction, CreatedAt = t.CreatedAt,
    };
}
