using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaseTrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCasePetitioner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Petitioner_Address",
                table: "Cases",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Petitioner_Email",
                table: "Cases",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Petitioner_Name",
                table: "Cases",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Petitioner_Phone",
                table: "Cases",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Petitioner_Address",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "Petitioner_Email",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "Petitioner_Name",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "Petitioner_Phone",
                table: "Cases");
        }
    }
}
