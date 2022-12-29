using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace clouddrop.Migrations
{
    /// <inheritdoc />
    public partial class contentstate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContentState",
                table: "Contents",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentState",
                table: "Contents");
        }
    }
}
