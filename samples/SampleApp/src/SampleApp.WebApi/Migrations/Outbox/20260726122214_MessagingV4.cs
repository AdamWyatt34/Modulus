using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SampleApp.WebApi.Migrations.Outbox
{
    /// <inheritdoc />
    public partial class MessagingV4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_ProcessedAt_CreatedAt",
                table: "OutboxMessages");

            migrationBuilder.AddColumn<string>(
                name: "ClaimedBy",
                table: "OutboxMessages",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedUntil",
                table: "OutboxMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptOnUtc",
                table: "OutboxMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledOnUtc",
                table: "OutboxMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                table: "OutboxMessages",
                type: "TEXT",
                maxLength: 55,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceState",
                table: "OutboxMessages",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedAt_NextAttemptOnUtc_ScheduledOnUtc_ClaimedUntil_CreatedAt",
                table: "OutboxMessages",
                columns: new[] { "ProcessedAt", "NextAttemptOnUtc", "ScheduledOnUtc", "ClaimedUntil", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_ProcessedAt_NextAttemptOnUtc_ScheduledOnUtc_ClaimedUntil_CreatedAt",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "ClaimedBy",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "ClaimedUntil",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "NextAttemptOnUtc",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "ScheduledOnUtc",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "TraceParent",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "TraceState",
                table: "OutboxMessages");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedAt_CreatedAt",
                table: "OutboxMessages",
                columns: new[] { "ProcessedAt", "CreatedAt" });
        }
    }
}
