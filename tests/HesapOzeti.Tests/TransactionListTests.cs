using HesapOzeti.Api.Data;
using HesapOzeti.Api.Statements;

namespace HesapOzeti.Tests;

public class TransactionListTests
{
    [Fact]
    public async Task Walking_the_pages_returns_every_row_once_newest_first()
    {
        await using var db = TestDb.NewContext();
        var account = await TestDb.NewAccountAsync(db);
        var noon = new DateTime(2026, 11, 5, 12, 0, 0);
        // Seven rows, three of them at the same second so the id has to break the tie, plus one duplicate.
        var dates = new[] { noon, noon, noon, noon.AddHours(-1), noon.AddHours(1), noon.AddDays(-1), noon.AddDays(-2) };
        var rows = dates.Select((d, i) => Row(account.Id, $"R{i}", d)).ToList();
        rows.Add(Row(account.Id, "R0", noon, ErpSyncStatus.Duplicate));
        db.BankTransactions.AddRange(rows);
        await db.SaveChangesAsync();
        db.ErpVouchers.Add(new ErpVoucher { BankTransactionId = rows[4].Id, VoucherNo = "TGR-X", VoucherType = "Tahsilat", Currency = "TRY" });
        db.ErpVouchers.Add(new ErpVoucher { BankTransactionId = rows[4].Id, VoucherNo = "TGR-OLD", VoucherType = "Tahsilat", Currency = "TRY", CancelledAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var seen = new List<TransactionRow>();
        Cursor? cursor = null;
        do
        {
            var page = (await TransactionList.GetPageAsync(db, account.Id, cursor, pageSize: 3))!;
            seen.AddRange(page.Items);
            cursor = Cursor.TryParse(page.NextCursor, out var c) ? c : null;
        } while (cursor is not null);

        var expected = rows.Take(7).OrderByDescending(r => r.TransactionDate).ThenByDescending(r => r.Id).Select(r => r.Id);
        Assert.Equal(expected, seen.Select(r => r.Id));
        Assert.Equal("TGR-X", seen.Single(r => r.Id == rows[4].Id).VoucherNo); // the live voucher, not the cancelled one
        Assert.Equal("Incoming", seen[0].Direction);
    }

    [Fact]
    public async Task Counts_the_total_only_on_the_first_page()
    {
        await using var db = TestDb.NewContext();
        var account = await TestDb.NewAccountAsync(db);
        db.BankTransactions.AddRange(Enumerable.Range(0, 3).Select(i => Row(account.Id, $"T{i}", new DateTime(2026, 1, 1).AddHours(i))));
        await db.SaveChangesAsync();

        var first = (await TransactionList.GetPageAsync(db, account.Id, null, pageSize: 2))!;
        Assert.True(Cursor.TryParse(first.NextCursor, out var c));
        var second = (await TransactionList.GetPageAsync(db, account.Id, c, pageSize: 2))!;

        Assert.Equal((3, null), (first.Total, second.Total));
        Assert.Null(second.NextCursor);
        Assert.Null(await TransactionList.GetPageAsync(db, -1, null, 2));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1-2-3")]
    [InlineData("99999999999999999999-1")]
    public void Rejects_a_broken_cursor(string value) => Assert.False(Cursor.TryParse(value, out _));

    static BankTransaction Row(int accountId, string reference, DateTime date, ErpSyncStatus status = ErpSyncStatus.Exported) => new()
    {
        BankAccountId = accountId, BankReferenceNo = reference, TransactionDate = date, Amount = 1m, Currency = "TRY",
        Description = "t", Direction = Direction.Incoming, ErpSyncStatus = status, CreatedAt = DateTime.UtcNow,
    };
}
