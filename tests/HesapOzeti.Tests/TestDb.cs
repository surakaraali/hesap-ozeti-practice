using HesapOzeti.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HesapOzeti.Tests;

/// <summary>A separate database on the docker-compose SQL Server, recreated once per test run.</summary>
public static class TestDb
{
    static readonly Lazy<string> Cs = new(() =>
    {
        LocalDb.LoadDotEnv();
        var cs = LocalDb.ConnectionString("HesapOzetiPractice_Tests");
        using var db = new AppDbContext(Options(cs));
        db.Database.EnsureDeleted();
        db.Database.Migrate();
        return cs;
    });

    static DbContextOptions<AppDbContext> Options(string cs) => new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options;

    public static AppDbContext NewContext() => new(Options(Cs.Value));

    public static IDbContextFactory<AppDbContext> Factory { get; } = new TestFactory();

    /// <summary>Each test makes its own account, so tests running in parallel do not see each other's rows.</summary>
    public static async Task<BankAccount> NewAccountAsync(AppDbContext db)
    {
        var account = new BankAccount { Iban = "TR" + Guid.NewGuid().ToString("N")[..24], BankName = "Demo Bank", Name = "Test", Currency = "TRY" };
        db.BankAccounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    class TestFactory : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => NewContext();
    }
}
