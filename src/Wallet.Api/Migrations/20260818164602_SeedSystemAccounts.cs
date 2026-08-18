using Microsoft.EntityFrameworkCore.Migrations;
using Wallet.Domain;

#nullable disable

namespace Wallet.Api.Migrations
{
    /// <inheritdoc />
    public partial class SeedSystemAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
                migrationBuilder.InsertData(
                    table: "wallets",
                    columns: new[] { "id", "currency", "created_at" },
                    values: new object[]
                    {

                        Guid.Parse("00000000-0000-0000-0000-000000000001")
                        , "AED"
                        , new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)   
                    });
        }
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
migrationBuilder.DeleteData(
                    table: "wallets",
                    keyColumn: "id",
                    keyValue: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        }
    }
}
