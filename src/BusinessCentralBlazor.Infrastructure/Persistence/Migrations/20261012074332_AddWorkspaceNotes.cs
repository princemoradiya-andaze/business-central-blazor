using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessCentralBlazor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkspaceNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceNotes_Companies_TenantId_CompanyId",
                        columns: x => new { x.TenantId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceNotes_TenantId_CompanyId_UpdatedAtUtc",
                table: "WorkspaceNotes",
                columns: new[] { "TenantId", "CompanyId", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkspaceNotes");
        }
    }
}
