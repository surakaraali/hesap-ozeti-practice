using HesapOzeti.Api.Data;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace HesapOzeti.Api.Erp;

/// <summary>Sends pending bank transactions to the ERP (Logo Tiger) as vouchers.</summary>
public class ErpExportService(AppDbContext db, FakeTigerClient tiger, ILogger<ErpExportService> log)
{
    public async Task<int> ExportPendingAsync(int batchSize = 500, CancellationToken ct = default)
    {
        var pending = await db.BankTransactions
            .Where(t => t.ErpSyncStatus == ErpSyncStatus.Pending)
            .OrderBy(t => t.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        var exported = 0;
        foreach (var tx in pending)
        {
            try
            {
                await tiger.CreateVoucherAsync(tx, ct);
                tx.ErpSyncStatus = ErpSyncStatus.Exported;
                await db.SaveChangesAsync(ct);
                exported++;
            }
            catch (TimeoutException ex)
            {
                // Tiger did not answer in time; the transaction stays Pending and is tried again on the next run.
                log.LogWarning(ex, "Tiger timed out for transaction {Id}, will retry on the next run", tx.Id);
            }
        }

        log.LogInformation("ERP export finished, {Exported}/{Total} exported", exported, pending.Count);
        return exported;
    }
}

/// <summary>
/// Simulates the Logo Tiger integration. Tiger is a separate system: it saves the voucher on its side
/// and sometimes answers too late, so the caller sees a timeout although the voucher exists.
/// </summary>
public class FakeTigerClient(IDbContextFactory<AppDbContext> tigerDb, IConfiguration config)
{
    public double TimeoutRate { get; set; } = config.GetValue("Erp:TimeoutRate", 0.05);

    public async Task<string> CreateVoucherAsync(BankTransaction tx, CancellationToken ct = default)
    {
        await using var db = await tigerDb.CreateDbContextAsync(ct);
        var voucher = new ErpVoucher
        {
            BankTransactionId = tx.Id,
            VoucherNo = "",
            VoucherType = tx.Direction == Direction.Incoming ? "Tahsilat" : "Ödeme",
            VoucherDate = tx.TransactionDate,
            Amount = tx.Amount,
            Currency = tx.Currency,
            CreatedAt = DateTime.UtcNow,
        };
        db.ErpVouchers.Add(voucher);
        await db.SaveChangesAsync(ct);
        voucher.VoucherNo = $"TGR-{voucher.Id:D9}";
        await db.SaveChangesAsync(ct);

        if (Random.Shared.NextDouble() < TimeoutRate)
            throw new TimeoutException("Tiger did not respond within 30 seconds.");
        return voucher.VoucherNo;
    }
}

public class ErpExportJob(ErpExportService export) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken ct) => await export.ExportPendingAsync(ct: ct);
}
