-- Execution plans and reads for the statement list (account 1, ~250k rows).
-- Run with sqlcmd -I (QUOTED_IDENTIFIER ON) or SQL Server ignores the filtered indexes.
-- OLD = the query shape before HOZ-104, NEW = after. Page position: 100,000 rows in.
SET NOCOUNT ON;
GO
SET SHOWPLAN_TEXT ON;
GO
-- OLD 1/3: total count
SELECT COUNT(*) FROM BankTransactions WHERE BankAccountId = 1 AND ErpSyncStatus <> 3;
-- OLD 2/3: whole entity, OFFSET paging
SELECT * FROM BankTransactions WHERE BankAccountId = 1 AND ErpSyncStatus <> 3
ORDER BY TransactionDate DESC OFFSET 100000 ROWS FETCH NEXT 50 ROWS ONLY;
-- OLD 3/3: run once per row (N+1), no CancelledAt condition
SELECT TOP(1) * FROM ErpVouchers WHERE BankTransactionId = 300154;
-- NEW: keyset page with the voucher as a subquery, one statement
SELECT TOP(50) b.Id, b.BankReferenceNo, b.TransactionDate, b.Amount, b.Currency, b.Description, b.Direction, b.ErpSyncStatus,
       (SELECT TOP(1) e.VoucherNo FROM ErpVouchers e WHERE e.BankTransactionId = b.Id AND e.CancelledAt IS NULL) AS VoucherNo
FROM BankTransactions b
WHERE b.BankAccountId = 1 AND b.ErpSyncStatus <> 3
  AND (b.TransactionDate < '2026-01-27T04:56:26' OR (b.TransactionDate = '2026-01-27T04:56:26' AND b.Id < 300154))
ORDER BY b.TransactionDate DESC, b.Id DESC;
GO
SET SHOWPLAN_TEXT OFF;
GO
SET STATISTICS IO ON;
GO
PRINT '== OLD count'; SELECT COUNT(*) FROM BankTransactions WHERE BankAccountId = 1 AND ErpSyncStatus <> 3;
PRINT '== OLD offset page'; SELECT * FROM BankTransactions WHERE BankAccountId = 1 AND ErpSyncStatus <> 3
ORDER BY TransactionDate DESC OFFSET 100000 ROWS FETCH NEXT 50 ROWS ONLY;
PRINT '== OLD one voucher lookup (x50 per page)'; SELECT TOP(1) * FROM ErpVouchers WHERE BankTransactionId = 300154;
PRINT '== NEW keyset page';
SELECT TOP(50) b.Id, b.BankReferenceNo, b.TransactionDate, b.Amount, b.Currency, b.Description, b.Direction, b.ErpSyncStatus,
       (SELECT TOP(1) e.VoucherNo FROM ErpVouchers e WHERE e.BankTransactionId = b.Id AND e.CancelledAt IS NULL) AS VoucherNo
FROM BankTransactions b
WHERE b.BankAccountId = 1 AND b.ErpSyncStatus <> 3
  AND (b.TransactionDate < '2026-01-27T04:56:26' OR (b.TransactionDate = '2026-01-27T04:56:26' AND b.Id < 300154))
ORDER BY b.TransactionDate DESC, b.Id DESC;
GO
