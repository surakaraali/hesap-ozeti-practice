-- Duplicates cleaned up by the PreventDuplicates migration, for accounting to reverse in Tiger.
-- Run: docker exec -i hesapozetipractice-db-1 /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -I -d HesapOzetiPractice -i /dev/stdin < scripts/duplicates-report.sql
SET NOCOUNT ON;

-- Cancelled vouchers, with the voucher that stays live for the same bank transaction.
SELECT v.VoucherNo AS CancelledVoucher, v.VoucherType, v.Amount, v.Currency, v.VoucherDate,
       t.BankAccountId, t.BankReferenceNo,
       CASE WHEN t.ErpSyncStatus = 3 THEN 'bank sent the transaction twice' ELSE 'export retried after a timeout' END AS Reason,
       COALESCE(live.VoucherNo, keep.VoucherNo) AS LiveVoucher
FROM ErpVouchers v
JOIN BankTransactions t ON t.Id = v.BankTransactionId
LEFT JOIN ErpVouchers live ON live.BankTransactionId = v.BankTransactionId AND live.CancelledAt IS NULL
OUTER APPLY (
    SELECT TOP 1 kv.VoucherNo FROM BankTransactions k
    JOIN ErpVouchers kv ON kv.BankTransactionId = k.Id AND kv.CancelledAt IS NULL
    WHERE k.BankAccountId = t.BankAccountId AND k.BankReferenceNo = t.BankReferenceNo AND k.ErpSyncStatus <> 3) keep
WHERE v.CancelledAt IS NOT NULL
ORDER BY t.BankAccountId, t.BankReferenceNo, v.Id;

-- Totals per currency (never add TRY and USD together)
SELECT v.Currency, COUNT(*) AS CancelledVouchers, SUM(v.Amount) AS CancelledAmount
FROM ErpVouchers v WHERE v.CancelledAt IS NOT NULL GROUP BY v.Currency;
SELECT COUNT(*) AS DuplicateTransactions FROM BankTransactions WHERE ErpSyncStatus = 3;
