using HesapOzeti.Api.Data;

namespace HesapOzeti.Tests;

public class SeederTests
{
    static readonly DateOnly Today = new(2026, 11, 15);

    [Fact]
    public void Same_seed_gives_the_same_rows()
    {
        var a = Seeder.GenerateRows(Seeder.Accounts, 1_000, Today).Select(r => (r.BankReferenceNo, r.Amount)).ToList();
        var b = Seeder.GenerateRows(Seeder.Accounts, 1_000, Today).Select(r => (r.BankReferenceNo, r.Amount)).ToList();
        Assert.Equal(a, b);
    }

    [Fact]
    public void Adds_a_few_duplicate_bank_references()
    {
        var rows = Seeder.GenerateRows(Seeder.Accounts, 100_000, Today).ToList();

        Assert.Equal(100_003, rows.Count); // rows 25k, 50k and 75k come twice
        Assert.Equal(3, rows.GroupBy(r => r.BankReferenceNo).Count(g => g.Count() > 1));
        Assert.All(rows, r => Assert.True(r.TransactionDate < Today.ToDateTime(TimeOnly.MinValue)));
    }
}
