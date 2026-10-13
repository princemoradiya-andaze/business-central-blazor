using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessCentralBlazor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_CompanyId_JournalBatchId",
                table: "InventoryMovements");

            migrationBuilder.AddColumn<Guid>(
                name: "ReceivablesAccountId",
                table: "Customers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalesOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ShipmentJournalBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ShipmentPostingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    InvoiceJournalBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrders", x => x.Id);
                    table.UniqueConstraint("AK_SalesOrders_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_SalesOrders_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrders_Customers_CompanyId_CustomerId",
                        columns: x => new { x.CompanyId, x.CustomerId },
                        principalTable: "Customers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrders_GeneralJournalBatches_CompanyId_InvoiceJournalBatchId",
                        columns: x => new { x.CompanyId, x.InvoiceJournalBatchId },
                        principalTable: "GeneralJournalBatches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrders_GeneralJournalBatches_CompanyId_ShipmentJournalBatchId",
                        columns: x => new { x.CompanyId, x.ShipmentJournalBatchId },
                        principalTable: "GeneralJournalBatches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JournalBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PostedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoices", x => x.Id);
                    table.UniqueConstraint("AK_SalesInvoices_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_SalesInvoices_Amounts", "[NetAmount] > 0 AND [TaxAmount] >= 0 AND [TotalAmount] = [NetAmount] + [TaxAmount]");
                    table.ForeignKey(
                        name: "FK_SalesInvoices_AspNetUsers_PostedByUserId",
                        column: x => x.PostedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoices_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoices_Customers_CompanyId_CustomerId",
                        columns: x => new { x.CompanyId, x.CustomerId },
                        principalTable: "Customers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoices_GeneralJournalBatches_CompanyId_JournalBatchId",
                        columns: x => new { x.CompanyId, x.JournalBatchId },
                        principalTable: "GeneralJournalBatches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoices_SalesOrders_CompanyId_SalesOrderId",
                        columns: x => new { x.CompanyId, x.SalesOrderId },
                        principalTable: "SalesOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    SalesAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderLines", x => x.Id);
                    table.UniqueConstraint("AK_SalesOrderLines_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_SalesOrderLines_PositiveQuantityAndPrice", "[Quantity] > 0 AND [UnitPrice] > 0");
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_GeneralLedgerAccounts_CompanyId_SalesAccountId",
                        columns: x => new { x.CompanyId, x.SalesAccountId },
                        principalTable: "GeneralLedgerAccounts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_Items_CompanyId_ItemId",
                        columns: x => new { x.CompanyId, x.ItemId },
                        principalTable: "Items",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_SalesOrders_CompanyId_SalesOrderId",
                        columns: x => new { x.CompanyId, x.SalesOrderId },
                        principalTable: "SalesOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_TaxCodes_CompanyId_TaxCodeId",
                        columns: x => new { x.CompanyId, x.TaxCodeId },
                        principalTable: "TaxCodes",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_CompanyId_JournalBatchId",
                table: "InventoryMovements",
                columns: new[] { "CompanyId", "JournalBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CompanyId_ReceivablesAccountId",
                table: "Customers",
                columns: new[] { "CompanyId", "ReceivablesAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_CompanyId_CustomerId",
                table: "SalesInvoices",
                columns: new[] { "CompanyId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_CompanyId_InvoiceNumber",
                table: "SalesInvoices",
                columns: new[] { "CompanyId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_CompanyId_JournalBatchId",
                table: "SalesInvoices",
                columns: new[] { "CompanyId", "JournalBatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_CompanyId_SalesOrderId",
                table: "SalesInvoices",
                columns: new[] { "CompanyId", "SalesOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_PostedByUserId",
                table: "SalesInvoices",
                column: "PostedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_CompanyId_ItemId",
                table: "SalesOrderLines",
                columns: new[] { "CompanyId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_CompanyId_SalesAccountId",
                table: "SalesOrderLines",
                columns: new[] { "CompanyId", "SalesAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_CompanyId_SalesOrderId_ItemId",
                table: "SalesOrderLines",
                columns: new[] { "CompanyId", "SalesOrderId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_CompanyId_SalesOrderId_LineNumber",
                table: "SalesOrderLines",
                columns: new[] { "CompanyId", "SalesOrderId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_CompanyId_TaxCodeId",
                table: "SalesOrderLines",
                columns: new[] { "CompanyId", "TaxCodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CompanyId_CustomerId",
                table: "SalesOrders",
                columns: new[] { "CompanyId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CompanyId_InvoiceJournalBatchId",
                table: "SalesOrders",
                columns: new[] { "CompanyId", "InvoiceJournalBatchId" },
                unique: true,
                filter: "[InvoiceJournalBatchId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CompanyId_OrderNumber",
                table: "SalesOrders",
                columns: new[] { "CompanyId", "OrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CompanyId_ShipmentJournalBatchId",
                table: "SalesOrders",
                columns: new[] { "CompanyId", "ShipmentJournalBatchId" },
                unique: true,
                filter: "[ShipmentJournalBatchId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_GeneralLedgerAccounts_CompanyId_ReceivablesAccountId",
                table: "Customers",
                columns: new[] { "CompanyId", "ReceivablesAccountId" },
                principalTable: "GeneralLedgerAccounts",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_GeneralLedgerAccounts_CompanyId_ReceivablesAccountId",
                table: "Customers");

            migrationBuilder.DropTable(
                name: "SalesInvoices");

            migrationBuilder.DropTable(
                name: "SalesOrderLines");

            migrationBuilder.DropTable(
                name: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_CompanyId_JournalBatchId",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_Customers_CompanyId_ReceivablesAccountId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "ReceivablesAccountId",
                table: "Customers");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_CompanyId_JournalBatchId",
                table: "InventoryMovements",
                columns: new[] { "CompanyId", "JournalBatchId" },
                unique: true);
        }
    }
}
