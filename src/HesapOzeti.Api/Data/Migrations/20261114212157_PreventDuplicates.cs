using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HesapOzeti.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PreventDuplicates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "ErpVouchers",
                type: "datetime2",
                nullable: true);

            // Existing duplicates would block the unique indexes. Nothing is deleted: the extra rows are marked,
            // and scripts/duplicates-report.sql lists them for accounting.
            // 1. Same bank reference twice: keep the exported row (or the oldest), mark the rest Duplicate (3).
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT ErpSyncStatus, ROW_NUMBER() OVER (
                        PARTITION BY BankAccountId, BankReferenceNo
                        ORDER BY CASE WHEN ErpSyncStatus = 1 THEN 0 ELSE 1 END, Id) AS rn
                    FROM BankTransactions)
                UPDATE ranked SET ErpSyncStatus = 3 WHERE rn > 1;
                """);

            // 2. Vouchers of those duplicate transactions are cancelled.
            migrationBuilder.Sql("""
                UPDATE v SET CancelledAt = SYSUTCDATETIME()
                FROM ErpVouchers v JOIN BankTransactions t ON t.Id = v.BankTransactionId
                WHERE t.ErpSyncStatus = 3 AND v.CancelledAt IS NULL;
                """);

            // 3. More than one voucher for the same transaction (retry after a Tiger timeout): keep the first one.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT CancelledAt, ROW_NUMBER() OVER (PARTITION BY BankTransactionId ORDER BY Id) AS rn
                    FROM ErpVouchers WHERE CancelledAt IS NULL)
                UPDATE ranked SET CancelledAt = SYSUTCDATETIME() WHERE rn > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ErpVouchers_BankTransactionId",
                table: "ErpVouchers",
                column: "BankTransactionId",
                unique: true,
                filter: "[CancelledAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_BankAccountId_BankReferenceNo",
                table: "BankTransactions",
                columns: new[] { "BankAccountId", "BankReferenceNo" },
                unique: true,
                filter: "[ErpSyncStatus] <> 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ErpVouchers_BankTransactionId",
                table: "ErpVouchers");

            migrationBuilder.DropIndex(
                name: "IX_BankTransactions_BankAccountId_BankReferenceNo",
                table: "BankTransactions");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "ErpVouchers");
        }
    }
}
