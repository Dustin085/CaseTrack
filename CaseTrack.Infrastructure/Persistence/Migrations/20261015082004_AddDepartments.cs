using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CaseTrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "Cases",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Departments",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { new Guid("05e97217-8eee-4551-8ff5-5bd9cb1762d0"), "ENV", "環境保護局" },
                    { new Guid("2f0c5ba0-9486-44b7-ab5b-a7032e4dc493"), "TRA", "交通局" },
                    { new Guid("f3c0543e-db7c-464b-8016-d71088ef6b83"), "PWD", "工務局" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cases_DepartmentId",
                table: "Cases",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Code",
                table: "Departments",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Cases_Departments_DepartmentId",
                table: "Cases",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Cases_Departments_DepartmentId",
                table: "Cases");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_Cases_DepartmentId",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "Cases");
        }
    }
}
