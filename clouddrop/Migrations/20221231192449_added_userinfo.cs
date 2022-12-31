using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace clouddrop.Migrations
{
    /// <inheritdoc />
    public partial class addeduserinfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: 1,
                column: "CreatedAt",
                value: 1672514689L);

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: 2,
                column: "CreatedAt",
                value: 1672514689L);

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "AvailableQuote", "CreatedAt" },
                values: new object[] { 10737418240L, 1672514689L });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: 1,
                column: "CreatedAt",
                value: 1672348329L);

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: 2,
                column: "CreatedAt",
                value: 1672348329L);

            migrationBuilder.UpdateData(
                table: "Plans",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "AvailableQuote", "CreatedAt" },
                values: new object[] { 10485760000L, 1672348329L });
        }
    }
}
