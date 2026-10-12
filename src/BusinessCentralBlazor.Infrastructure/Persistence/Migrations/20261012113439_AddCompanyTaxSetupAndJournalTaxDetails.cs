using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessCentralBlazor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyTaxSetupAndJournalTaxDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TaxBaseAmount",
                table: "GeneralLedgerEntries",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TaxCodeId",
                table: "GeneralLedgerEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxPostingType",
                table: "GeneralLedgerEntries",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TaxRateId",
                table: "GeneralLedgerEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRatePercent",
                table: "GeneralLedgerEntries",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxBaseAmount",
                table: "GeneralJournalLines",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TaxCodeId",
                table: "GeneralJournalLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxPostingType",
                table: "GeneralJournalLines",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TaxRateId",
                table: "GeneralJournalLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRatePercent",
                table: "GeneralJournalLines",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TaxCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    LiabilityAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecoverableAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxCodes", x => x.Id);
                    table.UniqueConstraint("AK_TaxCodes_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_TaxCodes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxCodes_GeneralLedgerAccounts_CompanyId_LiabilityAccountId",
                        columns: x => new { x.CompanyId, x.LiabilityAccountId },
                        principalTable: "GeneralLedgerAccounts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxCodes_GeneralLedgerAccounts_CompanyId_RecoverableAccountId",
                        columns: x => new { x.CompanyId, x.RecoverableAccountId },
                        principalTable: "GeneralLedgerAccounts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRates", x => x.Id);
                    table.UniqueConstraint("AK_TaxRates_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.UniqueConstraint("AK_TaxRates_CompanyId_TaxCodeId_Id", x => new { x.CompanyId, x.TaxCodeId, x.Id });
                    table.CheckConstraint("CK_TaxRates_DateRange", "[EndsOn] IS NULL OR [StartsOn] <= [EndsOn]");
                    table.CheckConstraint("CK_TaxRates_PositiveRate", "[RatePercent] > 0");
                    table.ForeignKey(
                        name: "FK_TaxRates_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxRates_TaxCodes_CompanyId_TaxCodeId",
                        columns: x => new { x.CompanyId, x.TaxCodeId },
                        principalTable: "TaxCodes",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerEntries_CompanyId_TaxCodeId_TaxRateId",
                table: "GeneralLedgerEntries",
                columns: new[] { "CompanyId", "TaxCodeId", "TaxRateId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_GeneralLedgerEntries_TaxDetails",
                table: "GeneralLedgerEntries",
                sql: "([TaxCodeId] IS NULL AND [TaxRateId] IS NULL AND [TaxRatePercent] IS NULL AND [TaxBaseAmount] IS NULL AND [TaxPostingType] IS NULL) OR ([TaxCodeId] IS NOT NULL AND [TaxRateId] IS NOT NULL AND [TaxRatePercent] > 0 AND [TaxBaseAmount] > 0 AND [TaxPostingType] IN ('Liability', 'Recoverable'))");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralJournalLines_CompanyId_TaxCodeId_TaxRateId",
                table: "GeneralJournalLines",
                columns: new[] { "CompanyId", "TaxCodeId", "TaxRateId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_GeneralJournalLines_TaxDetails",
                table: "GeneralJournalLines",
                sql: "([TaxCodeId] IS NULL AND [TaxRateId] IS NULL AND [TaxRatePercent] IS NULL AND [TaxBaseAmount] IS NULL AND [TaxPostingType] IS NULL) OR ([TaxCodeId] IS NOT NULL AND [TaxRateId] IS NOT NULL AND [TaxRatePercent] > 0 AND [TaxBaseAmount] > 0 AND [TaxPostingType] IN ('Liability', 'Recoverable'))");

            migrationBuilder.CreateIndex(
                name: "IX_TaxCodes_CompanyId_Code",
                table: "TaxCodes",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxCodes_CompanyId_LiabilityAccountId",
                table: "TaxCodes",
                columns: new[] { "CompanyId", "LiabilityAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxCodes_CompanyId_RecoverableAccountId",
                table: "TaxCodes",
                columns: new[] { "CompanyId", "RecoverableAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxRates_CompanyId_TaxCodeId_StartsOn",
                table: "TaxRates",
                columns: new[] { "CompanyId", "TaxCodeId", "StartsOn" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralJournalLines_TaxRates_CompanyId_TaxCodeId_TaxRateId",
                table: "GeneralJournalLines",
                columns: new[] { "CompanyId", "TaxCodeId", "TaxRateId" },
                principalTable: "TaxRates",
                principalColumns: new[] { "CompanyId", "TaxCodeId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralLedgerEntries_TaxRates_CompanyId_TaxCodeId_TaxRateId",
                table: "GeneralLedgerEntries",
                columns: new[] { "CompanyId", "TaxCodeId", "TaxRateId" },
                principalTable: "TaxRates",
                principalColumns: new[] { "CompanyId", "TaxCodeId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GeneralJournalLines_TaxRates_CompanyId_TaxCodeId_TaxRateId",
                table: "GeneralJournalLines");

            migrationBuilder.DropForeignKey(
                name: "FK_GeneralLedgerEntries_TaxRates_CompanyId_TaxCodeId_TaxRateId",
                table: "GeneralLedgerEntries");

            migrationBuilder.DropTable(
                name: "TaxRates");

            migrationBuilder.DropTable(
                name: "TaxCodes");

            migrationBuilder.DropIndex(
                name: "IX_GeneralLedgerEntries_CompanyId_TaxCodeId_TaxRateId",
                table: "GeneralLedgerEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GeneralLedgerEntries_TaxDetails",
                table: "GeneralLedgerEntries");

            migrationBuilder.DropIndex(
                name: "IX_GeneralJournalLines_CompanyId_TaxCodeId_TaxRateId",
                table: "GeneralJournalLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GeneralJournalLines_TaxDetails",
                table: "GeneralJournalLines");

            migrationBuilder.DropColumn(
                name: "TaxBaseAmount",
                table: "GeneralLedgerEntries");

            migrationBuilder.DropColumn(
                name: "TaxCodeId",
                table: "GeneralLedgerEntries");

            migrationBuilder.DropColumn(
                name: "TaxPostingType",
                table: "GeneralLedgerEntries");

            migrationBuilder.DropColumn(
                name: "TaxRateId",
                table: "GeneralLedgerEntries");

            migrationBuilder.DropColumn(
                name: "TaxRatePercent",
                table: "GeneralLedgerEntries");

            migrationBuilder.DropColumn(
                name: "TaxBaseAmount",
                table: "GeneralJournalLines");

            migrationBuilder.DropColumn(
                name: "TaxCodeId",
                table: "GeneralJournalLines");

            migrationBuilder.DropColumn(
                name: "TaxPostingType",
                table: "GeneralJournalLines");

            migrationBuilder.DropColumn(
                name: "TaxRateId",
                table: "GeneralJournalLines");

            migrationBuilder.DropColumn(
                name: "TaxRatePercent",
                table: "GeneralJournalLines");
        }
    }
}
