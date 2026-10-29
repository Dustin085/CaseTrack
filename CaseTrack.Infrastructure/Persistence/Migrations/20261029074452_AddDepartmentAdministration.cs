using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaseTrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Departments",
                type: "bit",
                nullable: false,
                // 既有的單位都維持啟用（EF 產生的預設是 false，非種子資料的單位會全部變成停用）
                defaultValue: true);

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: new Guid("05e97217-8eee-4551-8ff5-5bd9cb1762d0"),
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: new Guid("2f0c5ba0-9486-44b7-ab5b-a7032e4dc493"),
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "Departments",
                keyColumn: "Id",
                keyValue: new Guid("f3c0543e-db7c-464b-8016-d71088ef6b83"),
                column: "IsActive",
                value: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_ChangedAt",
                table: "AuditEntries",
                column: "ChangedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_ChangedAt",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Departments");
        }
    }
}
