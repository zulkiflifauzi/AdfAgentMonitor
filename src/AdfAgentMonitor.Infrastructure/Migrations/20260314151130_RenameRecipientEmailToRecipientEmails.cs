using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdfAgentMonitor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameRecipientEmailToRecipientEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RecipientEmail",
                table: "NotificationSettings",
                newName: "RecipientEmails");

            migrationBuilder.AlterColumn<string>(
                name: "RecipientEmails",
                table: "NotificationSettings",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RecipientEmails",
                table: "NotificationSettings",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000);

            migrationBuilder.RenameColumn(
                name: "RecipientEmails",
                table: "NotificationSettings",
                newName: "RecipientEmail");
        }
    }
}
