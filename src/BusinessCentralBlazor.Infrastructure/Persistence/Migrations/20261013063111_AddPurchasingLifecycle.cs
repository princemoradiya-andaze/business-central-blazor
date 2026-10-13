using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessCentralBlazor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchasingLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PayablesAccountId",
                table: "Vendors",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GoodsReceivedNotInvoicedAccountId",
                table: "CompanySettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VendorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ReceiptJournalBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReceiptPostingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    InvoiceJournalBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                    table.UniqueConstraint("AK_PurchaseOrders_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_GeneralJournalBatches_CompanyId_InvoiceJournalBatchId",
                        columns: x => new { x.CompanyId, x.InvoiceJournalBatchId },
                        principalTable: "GeneralJournalBatches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_GeneralJournalBatches_CompanyId_ReceiptJournalBatchId",
                        columns: x => new { x.CompanyId, x.ReceiptJournalBatchId },
                        principalTable: "GeneralJournalBatches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Vendors_CompanyId_VendorId",
                        columns: x => new { x.CompanyId, x.VendorId },
                        principalTable: "Vendors",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VendorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_PurchaseInvoices", x => x.Id);
                    table.UniqueConstraint("AK_PurchaseInvoices_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_PurchaseInvoices_Amounts", "[NetAmount] > 0 AND [TaxAmount] >= 0 AND [TotalAmount] = [NetAmount] + [TaxAmount]");
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_AspNetUsers_PostedByUserId",
                        column: x => x.PostedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_GeneralJournalBatches_CompanyId_JournalBatchId",
                        columns: x => new { x.CompanyId, x.JournalBatchId },
                        principalTable: "GeneralJournalBatches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_PurchaseOrders_CompanyId_PurchaseOrderId",
                        columns: x => new { x.CompanyId, x.PurchaseOrderId },
                        principalTable: "PurchaseOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_Vendors_CompanyId_VendorId",
                        columns: x => new { x.CompanyId, x.VendorId },
                        principalTable: "Vendors",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TaxCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderLines", x => x.Id);
                    table.UniqueConstraint("AK_PurchaseOrderLines_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_PurchaseOrderLines_PositiveQuantityAndCost", "[Quantity] > 0 AND [UnitCost] > 0");
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_Items_CompanyId_ItemId",
                        columns: x => new { x.CompanyId, x.ItemId },
                        principalTable: "Items",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_PurchaseOrders_CompanyId_PurchaseOrderId",
                        columns: x => new { x.CompanyId, x.PurchaseOrderId },
                        principalTable: "PurchaseOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_TaxCodes_CompanyId_TaxCodeId",
                        columns: x => new { x.CompanyId, x.TaxCodeId },
                        principalTable: "TaxCodes",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JournalBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PostedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReceipts", x => x.Id);
                    table.UniqueConstraint("AK_PurchaseReceipts_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_PurchaseReceipts_AspNetUsers_PostedByUserId",
                        column: x => x.PostedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceipts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceipts_GeneralJournalBatches_CompanyId_JournalBatchId",
                        columns: x => new { x.CompanyId, x.JournalBatchId },
                        principalTable: "GeneralJournalBatches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceipts_PurchaseOrders_CompanyId_PurchaseOrderId",
                        columns: x => new { x.CompanyId, x.PurchaseOrderId },
                        principalTable: "PurchaseOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_CompanyId_PayablesAccountId",
                table: "Vendors",
                columns: new[] { "CompanyId", "PayablesAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanySettings_CompanyId_GoodsReceivedNotInvoicedAccountId",
                table: "CompanySettings",
                columns: new[] { "CompanyId", "GoodsReceivedNotInvoicedAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_CompanyId_InvoiceNumber",
                table: "PurchaseInvoices",
                columns: new[] { "CompanyId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_CompanyId_JournalBatchId",
                table: "PurchaseInvoices",
                columns: new[] { "CompanyId", "JournalBatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_CompanyId_PurchaseOrderId",
                table: "PurchaseInvoices",
                columns: new[] { "CompanyId", "PurchaseOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_CompanyId_VendorId",
                table: "PurchaseInvoices",
                columns: new[] { "CompanyId", "VendorId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_PostedByUserId",
                table: "PurchaseInvoices",
                column: "PostedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_CompanyId_ItemId",
                table: "PurchaseOrderLines",
                columns: new[] { "CompanyId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_CompanyId_PurchaseOrderId_ItemId",
                table: "PurchaseOrderLines",
                columns: new[] { "CompanyId", "PurchaseOrderId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_CompanyId_PurchaseOrderId_LineNumber",
                table: "PurchaseOrderLines",
                columns: new[] { "CompanyId", "PurchaseOrderId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_CompanyId_TaxCodeId",
                table: "PurchaseOrderLines",
                columns: new[] { "CompanyId", "TaxCodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_CompanyId_InvoiceJournalBatchId",
                table: "PurchaseOrders",
                columns: new[] { "CompanyId", "InvoiceJournalBatchId" },
                unique: true,
                filter: "[InvoiceJournalBatchId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_CompanyId_OrderNumber",
                table: "PurchaseOrders",
                columns: new[] { "CompanyId", "OrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_CompanyId_ReceiptJournalBatchId",
                table: "PurchaseOrders",
                columns: new[] { "CompanyId", "ReceiptJournalBatchId" },
                unique: true,
                filter: "[ReceiptJournalBatchId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_CompanyId_VendorId",
                table: "PurchaseOrders",
                columns: new[] { "CompanyId", "VendorId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceipts_CompanyId_JournalBatchId",
                table: "PurchaseReceipts",
                columns: new[] { "CompanyId", "JournalBatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceipts_CompanyId_PurchaseOrderId",
                table: "PurchaseReceipts",
                columns: new[] { "CompanyId", "PurchaseOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceipts_PostedByUserId",
                table: "PurchaseReceipts",
                column: "PostedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CompanySettings_GeneralLedgerAccounts_CompanyId_GoodsReceivedNotInvoicedAccountId",
                table: "CompanySettings",
                columns: new[] { "CompanyId", "GoodsReceivedNotInvoicedAccountId" },
                principalTable: "GeneralLedgerAccounts",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Vendors_GeneralLedgerAccounts_CompanyId_PayablesAccountId",
                table: "Vendors",
                columns: new[] { "CompanyId", "PayablesAccountId" },
                principalTable: "GeneralLedgerAccounts",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CompanySettings_GeneralLedgerAccounts_CompanyId_GoodsReceivedNotInvoicedAccountId",
                table: "CompanySettings");

            migrationBuilder.DropForeignKey(
                name: "FK_Vendors_GeneralLedgerAccounts_CompanyId_PayablesAccountId",
                table: "Vendors");

            migrationBuilder.DropTable(
                name: "PurchaseInvoices");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLines");

            migrationBuilder.DropTable(
                name: "PurchaseReceipts");

            migrationBuilder.DropTable(
                name: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_Vendors_CompanyId_PayablesAccountId",
                table: "Vendors");

            migrationBuilder.DropIndex(
                name: "IX_CompanySettings_CompanyId_GoodsReceivedNotInvoicedAccountId",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "PayablesAccountId",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "GoodsReceivedNotInvoicedAccountId",
                table: "CompanySettings");
        }
    }
}
