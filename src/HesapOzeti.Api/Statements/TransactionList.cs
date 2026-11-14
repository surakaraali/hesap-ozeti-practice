using HesapOzeti.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HesapOzeti.Api.Statements;

public record TransactionRow(long Id, string BankReferenceNo, DateTime TransactionDate, decimal Amount, string Currency,
    string Description, string Direction, string ErpSyncStatus, string? VoucherNo);

/// <summary>Total is only counted for the first page; counting 250k rows on every scroll is wasted work.</summary>
public record TransactionPage(string Iban, int? Total, string? NextCursor, List<TransactionRow> Items);

/// <summary>
/// The account statement list, newest first. Uses keyset (cursor) paging: each page continues after the last row of
/// the previous one, so page 5000 costs the same as page 1. OFFSET would read and skip every earlier row.
/// </summary>
public static class TransactionList
{
    public const int MaxPageSize = 200;

    /// <returns>null when the account does not exist.</returns>
    public static async Task<TransactionPage?> GetPageAsync(AppDbContext db, int accountId, Cursor? after, int pageSize,
        DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var iban = await db.BankAccounts.Where(a => a.Id == accountId).Select(a => a.Iban).FirstOrDefaultAsync(ct);
        if (iban is null) return null;

        var query = db.BankTransactions.Where(t => t.BankAccountId == accountId && t.ErpSyncStatus != ErpSyncStatus.Duplicate);
        if (from is not null) query = query.Where(t => t.TransactionDate >= from);
        if (to is not null) query = query.Where(t => t.TransactionDate < to);

        int? total = after is null ? await query.CountAsync(ct) : null;

        if (after is { } c)
            query = query.Where(t => t.TransactionDate < c.Date || (t.TransactionDate == c.Date && t.Id < c.Id));

        // One SQL statement: a projection is not tracked, and the voucher comes from a subquery instead of
        // one query per row. "CancelledAt == null" lets SQL Server use the filtered unique index on ErpVouchers.
        var items = await query
            .OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.Id)
            .Take(pageSize)
            .Select(t => new TransactionRow(t.Id, t.BankReferenceNo, t.TransactionDate, t.Amount, t.Currency, t.Description,
                t.Direction.ToString(), t.ErpSyncStatus.ToString(),
                db.ErpVouchers.Where(v => v.BankTransactionId == t.Id && v.CancelledAt == null).Select(v => v.VoucherNo).FirstOrDefault()))
            .ToListAsync(ct);

        var next = items.Count == pageSize ? new Cursor(items[^1].TransactionDate, items[^1].Id).ToString() : null;
        return new TransactionPage(iban, total, next, items);
    }
}

/// <summary>Position after a row: its date and id (the id breaks ties between rows with the same date).</summary>
public readonly record struct Cursor(DateTime Date, long Id)
{
    public override string ToString() => $"{Date.Ticks}-{Id}";

    public static bool TryParse(string? s, out Cursor cursor)
    {
        cursor = default;
        var parts = s?.Split('-');
        if (parts is not [var ticks, var id] || !long.TryParse(ticks, out var t) || !long.TryParse(id, out var i)
            || t < DateTime.MinValue.Ticks || t > DateTime.MaxValue.Ticks)
            return false;
        cursor = new Cursor(new DateTime(t), i);
        return true;
    }
}
