using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessCentralBlazor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJournalBatchReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReversedByBatchId",
                table: "GeneralJournalBatches",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeneralJournalBatches_CompanyId_ReversedByBatchId",
                table: "GeneralJournalBatches",
                columns: new[] { "CompanyId", "ReversedByBatchId" },
                unique: true,
                filter: "[ReversedByBatchId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralJournalBatches_GeneralJournalBatches_CompanyId_ReversedByBatchId",
                table: "GeneralJournalBatches",
                columns: new[] { "CompanyId", "ReversedByBatchId" },
                principalTable: "GeneralJournalBatches",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GeneralJournalBatches_GeneralJournalBatches_CompanyId_ReversedByBatchId",
                table: "GeneralJournalBatches");

            migrationBuilder.DropIndex(
                name: "IX_GeneralJournalBatches_CompanyId_ReversedByBatchId",
                table: "GeneralJournalBatches");

            migrationBuilder.DropColumn(
                name: "ReversedByBatchId",
                table: "GeneralJournalBatches");
        }
    }
}
