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
            // Statement list: seek to the account, read newest first, stop after one page. Id breaks ties for the cursor.
            // ErpSyncStatus is included so the Duplicate filter and COUNT need no lookups into the table.
            // Not filtered, so it also serves the foreign key and any "WHERE BankAccountId = x".
            e.HasIndex(x => new { x.BankAccountId, x.TransactionDate, x.Id }).IncludeProperties(x => x.ErpSyncStatus);
            // The bank's reference is unique per account. Old duplicates are kept but left out of the index.
            e.HasIndex(x => new { x.BankAccountId, x.BankReferenceNo }).IsUnique().HasFilter("[ErpSyncStatus] <> 3");
        });

        b.Entity<ErpVoucher>(e =>
        {
            e.Property(x => x.VoucherNo).HasMaxLength(20);
            e.Property(x => x.VoucherType).HasMaxLength(20);
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.Property(x => x.Currency).HasMaxLength(3);
            // Idempotency key: one live voucher per bank transaction, however many times we send it.
            e.HasIndex(x => x.BankTransactionId).IsUnique().HasFilter("[CancelledAt] IS NULL");
        });
    }
}
