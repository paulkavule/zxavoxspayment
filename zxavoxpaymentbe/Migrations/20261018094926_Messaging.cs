using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace zxavoxpaymentbe.Migrations
{
    /// <inheritdoc />
    public partial class Messaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeadLetterItems",
                schema: "pay",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganisationId = table.Column<int>(type: "integer", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: true),
                    Consumer = table.Column<string>(type: "varchar(100)", nullable: false),
                    Queue = table.Column<string>(type: "varchar(150)", nullable: false),
                    Type = table.Column<string>(type: "varchar(100)", nullable: false),
                    TransactionId = table.Column<string>(type: "varchar(60)", nullable: false),
                    CorrelationId = table.Column<string>(type: "varchar(64)", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "varchar(1000)", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    DeadLetteredAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ActionedBy = table.Column<int>(type: "integer", nullable: true),
                    ActionedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    Note = table.Column<string>(type: "varchar(500)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeadLetterItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "pay",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "varchar(100)", nullable: false),
                    Exchange = table.Column<string>(type: "varchar(100)", nullable: false),
                    RoutingKey = table.Column<string>(type: "varchar(100)", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    CorrelationId = table.Column<string>(type: "varchar(64)", nullable: true),
                    TransactionId = table.Column<string>(type: "varchar(60)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    LastError = table.Column<string>(type: "varchar(500)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedMessages",
                schema: "pay",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Consumer = table.Column<string>(type: "varchar(100)", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedMessages", x => new { x.EventId, x.Consumer });
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeadLetterItems_Status",
                schema: "pay",
                table: "DeadLetterItems",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_EventId",
                schema: "pay",
                table: "OutboxMessages",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Unpublished",
                schema: "pay",
                table: "OutboxMessages",
                column: "Id",
                filter: "\"PublishedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeadLetterItems",
                schema: "pay");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "pay");

            migrationBuilder.DropTable(
                name: "ProcessedMessages",
                schema: "pay");
        }
    }
}
