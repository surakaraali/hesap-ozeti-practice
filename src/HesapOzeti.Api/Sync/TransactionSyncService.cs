using HesapOzeti.Api.Data;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace HesapOzeti.Api.Sync;

public record SyncResult(int Added, int FailedAccounts);

/// <summary>
/// Pulls yesterday's and today's transactions from the bank and saves the new ones. Looking at yesterday too means
/// transactions missed by a failed run just before midnight still come in.
/// </summary>
public class TransactionSyncService(AppDbContext db, BankApiClient bank, ILogger<TransactionSyncService> log)
{
    public async Task<SyncResult> SyncAsync(DateOnly today, CancellationToken ct = default)
    {
        var accounts = await db.BankAccounts.Where(a => a.IsActive).ToListAsync(ct);
        var days = new[] { today.AddDays(-1), today };
        var windowStart = days[0].ToDateTime(TimeOnly.MinValue);
        var added = 0;
        var failed = 0;

        foreach (var account in accounts)
        {
            List<BankTransactionDto> fromBank = [];
            try
            {
                foreach (var day in days)
                    fromBank.AddRange(await bank.GetTransactionsAsync(account.Iban, day, ct));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // One bad account must not stop the others. Nothing is lost: the next run asks for the same days again.
                log.LogError(ex, "Sync failed for account {AccountId} ({Iban})", account.Id, account.Iban);
                failed++;
                continue;
            }

            var known = await db.BankTransactions
                .Where(t => t.BankAccountId == account.Id && t.TransactionDate >= windowStart)
                .Select(t => t.BankReferenceNo)
                .ToListAsync(ct);

            foreach (var t in fromBank.Where(t => !known.Contains(t.ReferenceNo)))
            {
                db.BankTransactions.Add(new BankTransaction
                {
                    BankAccountId = account.Id,
                    BankReferenceNo = t.ReferenceNo,
                    TransactionDate = t.Date,
                    Amount = t.Amount,
                    Currency = t.Currency,
                    Description = t.Description,
                    Direction = t.Direction,
                    ErpSyncStatus = ErpSyncStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                });
                added++;
            }
        }

        await db.SaveChangesAsync(ct);
        if (failed > 0)
            log.LogWarning("Sync finished with errors: {Added} new transactions, {Failed}/{Total} accounts failed", added, failed, accounts.Count);
        else
            log.LogInformation("Sync finished: {Added} new transactions from {Total} accounts", added, accounts.Count);
        return new SyncResult(added, failed);
    }
}

public class TransactionSyncJob(TransactionSyncService sync) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken ct) =>
        await sync.SyncAsync(DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)), ct);
}
