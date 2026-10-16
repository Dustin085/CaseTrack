using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaseTrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalDataAccessLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PersonalDataAccessLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccessedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AccessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalDataAccessLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersonalDataAccessLogs_AccessedBy_AccessedAt",
                table: "PersonalDataAccessLogs",
                columns: new[] { "AccessedBy", "AccessedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonalDataAccessLogs_CaseId",
                table: "PersonalDataAccessLogs",
                column: "CaseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersonalDataAccessLogs");
        }
    }
}
