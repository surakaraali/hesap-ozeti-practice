using HesapOzeti.Api.Data;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace HesapOzeti.Api.Sync;

/// <summary>Pulls the day's transactions from the bank and saves the new ones.</summary>
public class TransactionSyncService(AppDbContext db, BankApiClient bank, ILogger<TransactionSyncService> log)
{
    public async Task<int> SyncAsync(DateOnly day, CancellationToken ct = default)
    {
        var accounts = await db.BankAccounts.Where(a => a.IsActive).ToListAsync(ct);
        var dayStart = day.ToDateTime(TimeOnly.MinValue);
        var added = 0;

        foreach (var account in accounts)
        {
            var fromBank = await bank.GetTransactionsAsync(account.Iban, day, ct);
            var known = await db.BankTransactions
                .Where(t => t.BankAccountId == account.Id && t.TransactionDate >= dayStart)
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
        log.LogInformation("Sync finished, {Count} new transactions", added);
        return added;
    }
}

public class TransactionSyncJob(TransactionSyncService sync) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken ct) =>
        await sync.SyncAsync(DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)), ct);
}
