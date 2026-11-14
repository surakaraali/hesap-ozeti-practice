using HesapOzeti.Api.Data;
using Microsoft.Data.SqlClient;
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
/// Creating a voucher is idempotent: the bank transaction id is the key, and a second call for the
/// same transaction returns the existing voucher instead of writing a new one.
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
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // The unique index says a live voucher already exists, e.g. our earlier call timed out after Tiger saved it.
            return await db.ErpVouchers.Where(v => v.BankTransactionId == tx.Id && v.CancelledAt == null)
                .Select(v => v.VoucherNo).SingleAsync(ct);
        }
        voucher.VoucherNo = $"TGR-{voucher.Id:D9}";
        await db.SaveChangesAsync(ct);

        if (Random.Shared.NextDouble() < TimeoutRate)
            throw new TimeoutException("Tiger did not respond within 30 seconds.");
        return voucher.VoucherNo;
    }
}

// Two overlapping runs would pick the same Pending rows.
[DisallowConcurrentExecution]
public class ErpExportJob(ErpExportService export) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken ct) => await export.ExportPendingAsync(ct: ct);
}
