using System.Data;
using System.Diagnostics;
using HesapOzeti.Api.FakeBank;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HesapOzeti.Api.Data;

/// <summary>Fills the database with about 500k made-up transactions over two years. Run with: dotnet run -- seed</summary>
public static class Seeder
{
    public const int DuplicateEvery = 25_000;

    public static readonly BankAccount[] Accounts =
    [
        new() { Iban = "TR000099990000000000000001", BankName = "Demo Bank", Name = "Ana TL Hesabı", Currency = "TRY" },
        new() { Iban = "TR000099990000000000000002", BankName = "Demo Bank", Name = "Tahsilat Hesabı", Currency = "TRY" },
        new() { Iban = "TR000099990000000000000003", BankName = "Demo Bank", Name = "Maaş Hesabı", Currency = "TRY" },
        new() { Iban = "TR000099990000000000000004", BankName = "Demo Bank", Name = "Kredi Kartı Tahsilat", Currency = "TRY" },
        new() { Iban = "TR000099990000000000000005", BankName = "Demo Bank", Name = "USD Hesabı", Currency = "USD" },
    ];

    // The main account gets half of the rows, so its statement screen is the slow one.
    static readonly double[] AccountShare = [0.50, 0.20, 0.15, 0.10, 0.05];

    public static async Task RunAsync(AppDbContext db, int count = 500_000)
    {
        await db.Database.MigrateAsync();
        if (await db.BankTransactions.AnyAsync())
        {
            Console.WriteLine("Database already has transactions. Drop it first: dotnet ef database drop -f -p src/HesapOzeti.Api");
            return;
        }

        var watch = Stopwatch.StartNew();
        var accounts = Accounts.Select(a => new BankAccount { Iban = a.Iban, BankName = a.BankName, Name = a.Name, Currency = a.Currency }).ToList();
        db.BankAccounts.AddRange(accounts);
        await db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var table = NewTable();
        using var bulk = new SqlBulkCopy(db.Database.GetConnectionString()) { DestinationTableName = "BankTransactions", BatchSize = 10_000, BulkCopyTimeout = 0 };
        foreach (DataColumn c in table.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);

        var written = 0;
        foreach (var t in GenerateRows(accounts, count, today))
        {
            table.Rows.Add(t.BankAccountId, t.BankReferenceNo, t.TransactionDate, t.Amount, t.Currency, t.Description, (int)t.Direction, (int)t.ErpSyncStatus, t.CreatedAt);
            if (table.Rows.Count < 50_000) continue;
            await bulk.WriteToServerAsync(table);
            written += table.Rows.Count;
            table.Clear();
            Console.WriteLine($"{written:N0} rows...");
        }
        await bulk.WriteToServerAsync(table);
        written += table.Rows.Count;

        // Exported rows already have a voucher in Tiger. One set-based INSERT is much faster than a loop.
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
        var vouchers = await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO ErpVouchers (BankTransactionId, VoucherNo, VoucherType, VoucherDate, Amount, Currency, CreatedAt)
            SELECT Id, 'TGR-' + RIGHT('000000000' + CAST(Id AS varchar(9)), 9),
                   CASE Direction WHEN 1 THEN N'Tahsilat' ELSE N'Ödeme' END, TransactionDate, Amount, Currency, CreatedAt
            FROM BankTransactions WHERE ErpSyncStatus = 1
            """);

        Console.WriteLine($"Seed done: {written:N0} transactions, {vouchers:N0} vouchers in {watch.Elapsed.TotalSeconds:N0} s");
    }

    /// <summary>
    /// Rows in date order, the same every time (fixed Random seed). The last two days are still Pending for the ERP.
    /// Every 25,000th row comes twice with the same bank reference, as when a bank sends a transaction again.
    /// </summary>
    public static IEnumerable<BankTransaction> GenerateRows(IReadOnlyList<BankAccount> accounts, int count, DateOnly today)
    {
        var rnd = new Random(42);
        var start = today.AddDays(-730).ToDateTime(TimeOnly.MinValue);
        var span = today.ToDateTime(TimeOnly.MinValue) - start;
        var pendingFrom = today.AddDays(-2).ToDateTime(TimeOnly.MinValue);

        for (var i = 0; i < count; i++)
        {
            var account = accounts[PickAccount(rnd)];
            var date = start.AddSeconds(Math.Floor(span.TotalSeconds * i / count));
            var direction = rnd.Next(2) == 0 ? Direction.Incoming : Direction.Outgoing;
            var row = new BankTransaction
            {
                BankAccountId = account.Id,
                BankReferenceNo = $"SD{date:yyyyMMdd}{i:D7}",
                TransactionDate = date,
                Amount = SampleData.Amount(rnd),
                Currency = account.Currency,
                Description = SampleData.Description(rnd, direction),
                Direction = direction,
                ErpSyncStatus = date >= pendingFrom ? ErpSyncStatus.Pending : ErpSyncStatus.Exported,
                CreatedAt = date.AddMinutes(5).AddHours(-3),
            };
            yield return row;

            if (i > 0 && i % DuplicateEvery == 0)
                yield return new BankTransaction
                {
                    BankAccountId = row.BankAccountId, BankReferenceNo = row.BankReferenceNo, TransactionDate = row.TransactionDate,
                    Amount = row.Amount, Currency = row.Currency, Description = row.Description, Direction = row.Direction,
                    ErpSyncStatus = ErpSyncStatus.Pending, CreatedAt = row.CreatedAt.AddDays(1),
                };
        }
    }

    static int PickAccount(Random rnd)
    {
        var roll = rnd.NextDouble();
        for (var i = 0; i < AccountShare.Length; i++)
            if ((roll -= AccountShare[i]) < 0) return i;
        return AccountShare.Length - 1;
    }

    static DataTable NewTable()
    {
        var t = new DataTable();
        t.Columns.Add("BankAccountId", typeof(int));
        t.Columns.Add("BankReferenceNo", typeof(string));
        t.Columns.Add("TransactionDate", typeof(DateTime));
        t.Columns.Add("Amount", typeof(decimal));
        t.Columns.Add("Currency", typeof(string));
        t.Columns.Add("Description", typeof(string));
        t.Columns.Add("Direction", typeof(int));
        t.Columns.Add("ErpSyncStatus", typeof(int));
        t.Columns.Add("CreatedAt", typeof(DateTime));
        return t;
    }
}
