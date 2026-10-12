using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessCentralBlazor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignJournalAndAccountingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM dbo.AccountingPeriods
                    GROUP BY CompanyId, StartsOn
                    HAVING COUNT(*) > 1
                )
                    THROW 51110, 'Cannot align accounting periods: duplicate start dates exist within a company.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_GeneralJournalBatches_AccountingPeriods_CompanyId_AccountingPeriodId",
                table: "GeneralJournalBatches");

            migrationBuilder.DropIndex(
                name: "IX_GeneralJournalBatches_CompanyId_AccountingPeriodId",
                table: "GeneralJournalBatches");

            migrationBuilder.DropIndex(
                name: "IX_GeneralJournalBatches_CompanyId_Number",
                table: "GeneralJournalBatches");

            migrationBuilder.DropIndex(
                name: "IX_AccountingPeriods_CompanyId_Code",
                table: "AccountingPeriods");

            migrationBuilder.RenameColumn(
                name: "Type",
                table: "GeneralLedgerAccounts",
                newName: "Category");

            migrationBuilder.RenameColumn(
                name: "Number",
                table: "GeneralJournalBatches",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "AccountingPeriodId",
                table: "GeneralJournalBatches",
                newName: "DefaultAccountingPeriodId");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "AccountingPeriods",
                newName: "Name");

            migrationBuilder.CreateTable(
                name: "GeneralJournalTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneralJournalTemplates", x => x.Id);
                    table.UniqueConstraint("AK_GeneralJournalTemplates_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_GeneralJournalTemplates_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddColumn<Guid>(
                name: "AccountingPeriodId",
                table: "GeneralJournalLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "GeneralJournalBatches",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "JournalTemplateId",
                table: "GeneralJournalBatches",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "GeneralLedgerAccounts",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DateLocked",
                table: "AccountingPeriods",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsClosed",
                table: "AccountingPeriods",
                type: "bit",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE dbo.AccountingPeriods
                SET IsClosed = CASE WHEN Status = N'Closed' THEN 1 ELSE 0 END,
                    DateLocked = 0;

                UPDATE dbo.GeneralLedgerAccounts SET Type = N'Posting';
                UPDATE dbo.GeneralJournalBatches SET Description = N'';

                INSERT dbo.GeneralJournalTemplates (Id, CompanyId, Code, Description, Type)
                SELECT NEWID(), Id, N'GENERAL', N'General journal', N'General'
                FROM dbo.Companies;

                UPDATE batch
                SET JournalTemplateId = template.Id
                FROM dbo.GeneralJournalBatches AS batch
                INNER JOIN dbo.GeneralJournalTemplates AS template
                    ON template.CompanyId = batch.CompanyId
                    AND template.Code = N'GENERAL';

                UPDATE line
                SET AccountingPeriodId = batch.DefaultAccountingPeriodId
                FROM dbo.GeneralJournalLines AS line
                INNER JOIN dbo.GeneralJournalBatches AS batch
                    ON batch.CompanyId = line.CompanyId
                    AND batch.Id = line.JournalBatchId;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "AccountingPeriodId",
                table: "GeneralJournalLines",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "GeneralJournalBatches",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "JournalTemplateId",
                table: "GeneralJournalBatches",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "GeneralLedgerAccounts",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "DateLocked",
                table: "AccountingPeriods",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsClosed",
                table: "AccountingPeriods",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "Status",
                table: "AccountingPeriods");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralJournalLines_CompanyId_AccountingPeriodId",
                table: "GeneralJournalLines",
                columns: new[] { "CompanyId", "AccountingPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_GeneralJournalBatches_CompanyId_DefaultAccountingPeriodId",
                table: "GeneralJournalBatches",
                columns: new[] { "CompanyId", "DefaultAccountingPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_GeneralJournalBatches_CompanyId_JournalTemplateId_Name",
                table: "GeneralJournalBatches",
                columns: new[] { "CompanyId", "JournalTemplateId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountingPeriods_CompanyId_StartsOn",
                table: "AccountingPeriods",
                columns: new[] { "CompanyId", "StartsOn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeneralJournalTemplates_CompanyId_Code",
                table: "GeneralJournalTemplates",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralJournalBatches_AccountingPeriods_CompanyId_DefaultAccountingPeriodId",
                table: "GeneralJournalBatches",
                columns: new[] { "CompanyId", "DefaultAccountingPeriodId" },
                principalTable: "AccountingPeriods",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralJournalBatches_GeneralJournalTemplates_CompanyId_JournalTemplateId",
                table: "GeneralJournalBatches",
                columns: new[] { "CompanyId", "JournalTemplateId" },
                principalTable: "GeneralJournalTemplates",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralJournalLines_AccountingPeriods_CompanyId_AccountingPeriodId",
                table: "GeneralJournalLines",
                columns: new[] { "CompanyId", "AccountingPeriodId" },
                principalTable: "AccountingPeriods",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM dbo.GeneralJournalLines AS line
                    INNER JOIN dbo.GeneralJournalBatches AS batch
                        ON batch.CompanyId = line.CompanyId
                        AND batch.Id = line.JournalBatchId
                    WHERE line.AccountingPeriodId <> batch.DefaultAccountingPeriodId
                )
                    THROW 51111, 'Cannot revert: journal lines use periods different from their batch default.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM dbo.AccountingPeriods
                    GROUP BY CompanyId, Name
                    HAVING COUNT(*) > 1
                )
                    THROW 51112, 'Cannot revert: accounting period names are not unique within a company.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM dbo.GeneralJournalBatches
                    GROUP BY CompanyId, Name
                    HAVING COUNT(*) > 1
                )
                    THROW 51113, 'Cannot revert: journal batch names are not unique within a company.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_GeneralJournalBatches_AccountingPeriods_CompanyId_DefaultAccountingPeriodId",
                table: "GeneralJournalBatches");

            migrationBuilder.DropForeignKey(
                name: "FK_GeneralJournalBatches_GeneralJournalTemplates_CompanyId_JournalTemplateId",
                table: "GeneralJournalBatches");

            migrationBuilder.DropForeignKey(
                name: "FK_GeneralJournalLines_AccountingPeriods_CompanyId_AccountingPeriodId",
                table: "GeneralJournalLines");

            migrationBuilder.DropIndex(
                name: "IX_GeneralJournalLines_CompanyId_AccountingPeriodId",
                table: "GeneralJournalLines");

            migrationBuilder.DropIndex(
                name: "IX_GeneralJournalBatches_CompanyId_DefaultAccountingPeriodId",
                table: "GeneralJournalBatches");

            migrationBuilder.DropIndex(
                name: "IX_GeneralJournalBatches_CompanyId_JournalTemplateId_Name",
                table: "GeneralJournalBatches");

            migrationBuilder.DropIndex(
                name: "IX_AccountingPeriods_CompanyId_StartsOn",
                table: "AccountingPeriods");

            migrationBuilder.DropColumn(
                name: "AccountingPeriodId",
                table: "GeneralJournalLines");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "GeneralJournalBatches");

            migrationBuilder.DropColumn(
                name: "JournalTemplateId",
                table: "GeneralJournalBatches");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "GeneralLedgerAccounts");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "AccountingPeriods",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE dbo.AccountingPeriods
                SET Status = CASE WHEN IsClosed = 1 THEN N'Closed' ELSE N'Open' END;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "AccountingPeriods",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "DateLocked",
                table: "AccountingPeriods");

            migrationBuilder.DropColumn(
                name: "IsClosed",
                table: "AccountingPeriods");

            migrationBuilder.RenameColumn(
                name: "Category",
                table: "GeneralLedgerAccounts",
                newName: "Type");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "GeneralJournalBatches",
                newName: "Number");

            migrationBuilder.RenameColumn(
                name: "DefaultAccountingPeriodId",
                table: "GeneralJournalBatches",
                newName: "AccountingPeriodId");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "AccountingPeriods",
                newName: "Code");

            migrationBuilder.DropTable(
                name: "GeneralJournalTemplates");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralJournalBatches_CompanyId_AccountingPeriodId",
                table: "GeneralJournalBatches",
                columns: new[] { "CompanyId", "AccountingPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_GeneralJournalBatches_CompanyId_Number",
                table: "GeneralJournalBatches",
                columns: new[] { "CompanyId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountingPeriods_CompanyId_Code",
                table: "AccountingPeriods",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralJournalBatches_AccountingPeriods_CompanyId_AccountingPeriodId",
                table: "GeneralJournalBatches",
                columns: new[] { "CompanyId", "AccountingPeriodId" },
                principalTable: "AccountingPeriods",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
