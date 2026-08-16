using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AK.Notification.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Recipient = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ChannelType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_history", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notification_history_CorrelationId",
                table: "notification_history",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_history_CreatedAt",
                table: "notification_history",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_history");
        }
    }
}
