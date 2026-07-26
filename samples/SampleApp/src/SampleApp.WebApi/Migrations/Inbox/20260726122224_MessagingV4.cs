using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SampleApp.WebApi.Migrations.Inbox
{
    /// <inheritdoc />
    public partial class MessagingV4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InboxMessages_ProcessedOnUtc_OccurredOnUtc",
                table: "InboxMessages");

            migrationBuilder.DropColumn(
                name: "ProcessedOnUtc",
                table: "InboxMessages");

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessedOnUtc",
                table: "InboxMessageConsumers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReservedOnUtc",
                table: "InboxMessageConsumers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_OccurredOnUtc",
                table: "InboxMessages",
                column: "OccurredOnUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InboxMessages_OccurredOnUtc",
                table: "InboxMessages");

            migrationBuilder.DropColumn(
                name: "ProcessedOnUtc",
                table: "InboxMessageConsumers");

            migrationBuilder.DropColumn(
                name: "ReservedOnUtc",
                table: "InboxMessageConsumers");

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessedOnUtc",
                table: "InboxMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_ProcessedOnUtc_OccurredOnUtc",
                table: "InboxMessages",
                columns: new[] { "ProcessedOnUtc", "OccurredOnUtc" });
        }
    }
}
