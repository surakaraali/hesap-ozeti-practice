using Microsoft.EntityFrameworkCore;

namespace HesapOzeti.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();
    public DbSet<ErpVoucher> ErpVouchers => Set<ErpVoucher>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<BankAccount>(e =>
        {
            e.Property(x => x.Iban).HasMaxLength(34);
            e.Property(x => x.BankName).HasMaxLength(100);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.HasIndex(x => x.Iban).IsUnique();
        });

        b.Entity<BankTransaction>(e =>
        {
            e.Property(x => x.BankReferenceNo).HasMaxLength(50);
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Description).HasMaxLength(250);
        });

        b.Entity<ErpVoucher>(e =>
        {
            e.Property(x => x.VoucherNo).HasMaxLength(20);
            e.Property(x => x.VoucherType).HasMaxLength(20);
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.Property(x => x.Currency).HasMaxLength(3);
        });
    }
}
