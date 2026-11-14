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
        var account = await TestDb.NewAccountAsync(db);
        var tx = new BankTransaction
        {
            BankAccountId = account.Id, BankReferenceNo = "R-" + Guid.NewGuid().ToString("N")[..8], TransactionDate = DateTime.Today,
            Amount = 100m, Currency = "TRY", Description = "test", Direction = Direction.Incoming, CreatedAt = DateTime.UtcNow,
        };
        db.BankTransactions.Add(tx);
        await db.SaveChangesAsync();

        var tiger = new FakeTigerClient(TestDb.Factory, new ConfigurationBuilder().Build()) { TimeoutRate = 0 };
        await new ErpExportService(db, tiger, NullLogger<ErpExportService>.Instance).ExportPendingAsync(batchSize: 10_000);

        Assert.Equal(ErpSyncStatus.Exported, tx.ErpSyncStatus);
        var voucher = await db.ErpVouchers.SingleAsync(v => v.BankTransactionId == tx.Id);
        Assert.Equal(("Tahsilat", 100m), (voucher.VoucherType, voucher.Amount));
    }
}
