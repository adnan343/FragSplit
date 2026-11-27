using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shop.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class VialMuseCodLaunch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReceiptLinkRequestedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdminTotpKey",
                table: "Accounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AdminTotpStep",
                table: "Accounts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Enabled",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql("""
                ALTER TABLE "Accounts" ALTER COLUMN "Enabled" DROP DEFAULT;
                UPDATE "Settings" SET "Name" = 'VialMuse' WHERE "Name" = 'FragSplit';
                UPDATE "Orders" SET "ExpiresAt" = "CreatedAt" + INTERVAL '24 hours'
                WHERE "Method" = 'COD' AND "Fulfillment" = 'Pending'
                  AND "StockState" = 'Reserved' AND "ExpiresAt" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Method_Fulfillment_StockState_ExpiresAt",
                table: "Orders",
                columns: new[] { "Method", "Fulfillment", "StockState", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_Method_Fulfillment_StockState_ExpiresAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ReceiptLinkRequestedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AdminTotpKey",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "AdminTotpStep",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Enabled",
                table: "Accounts");
        }
    }
}
