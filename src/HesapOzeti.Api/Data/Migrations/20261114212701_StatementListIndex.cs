using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HesapOzeti.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class StatementListIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BankTransactions_BankAccountId",
                table: "BankTransactions");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_BankAccountId_TransactionDate_Id",
                table: "BankTransactions",
                columns: new[] { "BankAccountId", "TransactionDate", "Id" })
                .Annotation("SqlServer:Include", new[] { "ErpSyncStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BankTransactions_BankAccountId_TransactionDate_Id",
                table: "BankTransactions");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_BankAccountId",
                table: "BankTransactions",
                column: "BankAccountId");
        }
    }
}
