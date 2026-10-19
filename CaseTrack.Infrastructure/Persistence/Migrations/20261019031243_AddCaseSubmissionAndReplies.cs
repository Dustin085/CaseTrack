using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaseTrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseSubmissionAndReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubmittedContent",
                table: "SupplementRequests",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosingReply",
                table: "Cases",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SubmittedAt",
                table: "Cases",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "SubmittedBy",
                table: "Cases",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_SubmittedBy",
                table: "Cases",
                column: "SubmittedBy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cases_SubmittedBy",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "SubmittedContent",
                table: "SupplementRequests");

            migrationBuilder.DropColumn(
                name: "ClosingReply",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "SubmittedBy",
                table: "Cases");
        }
    }
}
