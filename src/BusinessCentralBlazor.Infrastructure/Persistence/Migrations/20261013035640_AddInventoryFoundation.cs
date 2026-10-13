using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessCentralBlazor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InventoryAccountId",
                table: "Items",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryIncreaseOffsetAccountId",
                table: "Items",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryIssueExpenseAccountId",
                table: "Items",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InventoryValue",
                table: "Items",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityOnHand",
                table: "Items",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Items",
                type: "rowversion",
                rowVersion: true,
                nullable: false);

            migrationBuilder.CreateTable(
                name: "InventoryMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountingPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JournalBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    InventoryValue = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PostedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryMovements", x => x.Id);
                    table.UniqueConstraint("AK_InventoryMovements_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_InventoryMovements_NonNegativeCost", "[UnitCost] >= 0 AND [InventoryValue] > 0");
                    table.CheckConstraint("CK_InventoryMovements_NonzeroQuantity", "[Quantity] <> 0");
                    table.ForeignKey(
                        name: "FK_InventoryMovements_AccountingPeriods_CompanyId_AccountingPeriodId",
                        columns: x => new { x.CompanyId, x.AccountingPeriodId },
                        principalTable: "AccountingPeriods",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_AspNetUsers_PostedByUserId",
                        column: x => x.PostedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_GeneralJournalBatches_CompanyId_JournalBatchId",
                        columns: x => new { x.CompanyId, x.JournalBatchId },
                        principalTable: "GeneralJournalBatches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_Items_CompanyId_ItemId",
                        columns: x => new { x.CompanyId, x.ItemId },
                        principalTable: "Items",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Items_CompanyId_InventoryAccountId",
                table: "Items",
                columns: new[] { "CompanyId", "InventoryAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_Items_CompanyId_InventoryIncreaseOffsetAccountId",
                table: "Items",
                columns: new[] { "CompanyId", "InventoryIncreaseOffsetAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_Items_CompanyId_InventoryIssueExpenseAccountId",
                table: "Items",
                columns: new[] { "CompanyId", "InventoryIssueExpenseAccountId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Items_NonNegativeInventory",
                table: "Items",
                sql: "[QuantityOnHand] >= 0 AND [InventoryValue] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_CompanyId_AccountingPeriodId",
                table: "InventoryMovements",
                columns: new[] { "CompanyId", "AccountingPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_CompanyId_DocumentNumber",
                table: "InventoryMovements",
                columns: new[] { "CompanyId", "DocumentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_CompanyId_ItemId",
                table: "InventoryMovements",
                columns: new[] { "CompanyId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_CompanyId_JournalBatchId",
                table: "InventoryMovements",
                columns: new[] { "CompanyId", "JournalBatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_PostedByUserId",
                table: "InventoryMovements",
                column: "PostedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Items_GeneralLedgerAccounts_CompanyId_InventoryAccountId",
                table: "Items",
                columns: new[] { "CompanyId", "InventoryAccountId" },
                principalTable: "GeneralLedgerAccounts",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Items_GeneralLedgerAccounts_CompanyId_InventoryIncreaseOffsetAccountId",
                table: "Items",
                columns: new[] { "CompanyId", "InventoryIncreaseOffsetAccountId" },
                principalTable: "GeneralLedgerAccounts",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Items_GeneralLedgerAccounts_CompanyId_InventoryIssueExpenseAccountId",
                table: "Items",
                columns: new[] { "CompanyId", "InventoryIssueExpenseAccountId" },
                principalTable: "GeneralLedgerAccounts",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Items_GeneralLedgerAccounts_CompanyId_InventoryAccountId",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_Items_GeneralLedgerAccounts_CompanyId_InventoryIncreaseOffsetAccountId",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_Items_GeneralLedgerAccounts_CompanyId_InventoryIssueExpenseAccountId",
                table: "Items");

            migrationBuilder.DropTable(
                name: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_Items_CompanyId_InventoryAccountId",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Items_CompanyId_InventoryIncreaseOffsetAccountId",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Items_CompanyId_InventoryIssueExpenseAccountId",
                table: "Items");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Items_NonNegativeInventory",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "InventoryAccountId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "InventoryIncreaseOffsetAccountId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "InventoryIssueExpenseAccountId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "InventoryValue",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "QuantityOnHand",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Items");
        }
    }
}
