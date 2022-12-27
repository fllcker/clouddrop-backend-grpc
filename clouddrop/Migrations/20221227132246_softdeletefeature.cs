using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace clouddrop.Migrations
{
    /// <inheritdoc />
    public partial class softdeletefeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Contents",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Contents");
        }
    }
}
