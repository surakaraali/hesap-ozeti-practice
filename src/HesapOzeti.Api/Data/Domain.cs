namespace HesapOzeti.Api.Data;

public class BankAccount
{
    public int Id { get; set; }
    public required string Iban { get; set; }
    public required string BankName { get; set; }
    public required string Name { get; set; }
    public required string Currency { get; set; }
    public bool IsActive { get; set; } = true;

    public List<BankTransaction> Transactions { get; set; } = [];
}

public enum Direction { Incoming = 1, Outgoing = 2 }

public enum ErpSyncStatus { Pending = 0, Exported = 1, Failed = 2 }

public class BankTransaction
{
    public long Id { get; set; }
    public int BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    public required string BankReferenceNo { get; set; }
    public DateTime TransactionDate { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public required string Description { get; set; }
    public Direction Direction { get; set; }
    public ErpSyncStatus ErpSyncStatus { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Stands in for a voucher (fiş) in Logo Tiger. Tiger is a separate system, so the voucher only keeps
/// the transaction id as a number, without a foreign key.
/// </summary>
public class ErpVoucher
{
    public long Id { get; set; }
    public long BankTransactionId { get; set; }
    public required string VoucherNo { get; set; }
    public required string VoucherType { get; set; }
    public DateTime VoucherDate { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public DateTime CreatedAt { get; set; }
}
