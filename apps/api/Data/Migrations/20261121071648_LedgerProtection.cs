using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shop.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class LedgerProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION reject_stock_ledger_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Inventory movements are append-only';
                END;
                $$;
                CREATE TRIGGER immutable_stock_ledger BEFORE UPDATE OR DELETE ON "Movements"
                FOR EACH ROW EXECUTE FUNCTION reject_stock_ledger_change();
                """);
            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedAt",
                table: "Orders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PaidAt",
                table: "Orders",
                column: "PaidAt");

            migrationBuilder.CreateIndex(
                name: "IX_History_Event_At",
                table: "History",
                columns: new[] { "Event", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER immutable_stock_ledger ON "Movements";
                DROP FUNCTION reject_stock_ledger_change();
                """);
            migrationBuilder.DropIndex(
                name: "IX_Orders_CreatedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_PaidAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_History_Event_At",
                table: "History");
        }
    }
}
