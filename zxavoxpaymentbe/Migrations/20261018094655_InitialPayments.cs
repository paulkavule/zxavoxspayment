using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace zxavoxpaymentbe.Migrations
{
    /// <inheritdoc />
    public partial class InitialPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments_api");

            migrationBuilder.CreateTable(
                name: "ApiClients",
                schema: "payments_api",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "varchar(60)", nullable: false),
                    KeyHash = table.Column<string>(type: "varchar(64)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiClients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BeneficiaryLookups",
                schema: "payments_api",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganisationId = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "varchar(20)", nullable: false),
                    MaskedMsisdn = table.Column<string>(type: "varchar(20)", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", nullable: false),
                    Error = table.Column<string>(type: "varchar(200)", nullable: false),
                    LookedUpAt = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BeneficiaryLookups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CallbackReceipts",
                schema: "payments_api",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Provider = table.Column<string>(type: "varchar(20)", nullable: false),
                    ReferenceId = table.Column<string>(type: "varchar(100)", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Verified = table.Column<bool>(type: "boolean", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallbackReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderAccounts",
                schema: "payments_api",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganisationId = table.Column<int>(type: "integer", nullable: true),
                    Provider = table.Column<string>(type: "varchar(20)", nullable: false),
                    BaseUrl = table.Column<string>(type: "varchar(200)", nullable: false),
                    Country = table.Column<string>(type: "varchar(2)", nullable: false),
                    Currency = table.Column<string>(type: "varchar(3)", nullable: false),
                    DefaultMethod = table.Column<string>(type: "varchar(10)", nullable: false),
                    TargetEnvironment = table.Column<string>(type: "varchar(40)", nullable: false),
                    TransactionType = table.Column<string>(type: "varchar(10)", nullable: false),
                    MessageSigningEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Secrets = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderCalls",
                schema: "payments_api",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PaymentOrderId = table.Column<long>(type: "bigint", nullable: true),
                    Provider = table.Column<string>(type: "varchar(20)", nullable: false),
                    Operation = table.Column<string>(type: "varchar(40)", nullable: false),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    Request = table.Column<string>(type: "text", nullable: false),
                    Response = table.Column<string>(type: "text", nullable: false),
                    CorrelationId = table.Column<string>(type: "varchar(64)", nullable: false),
                    At = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderCalls", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentOrders",
                schema: "payments_api",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientId = table.Column<int>(type: "integer", nullable: false),
                    OrganisationId = table.Column<int>(type: "integer", nullable: false),
                    ExternalRef = table.Column<string>(type: "varchar(60)", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<string>(type: "varchar(64)", nullable: false),
                    Provider = table.Column<string>(type: "varchar(20)", nullable: false),
                    ProviderAccountId = table.Column<int>(type: "integer", nullable: true),
                    Method = table.Column<string>(type: "varchar(10)", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "varchar(3)", nullable: false),
                    PayeeType = table.Column<string>(type: "varchar(10)", nullable: false),
                    PayeeId = table.Column<string>(type: "varchar(40)", nullable: false),
                    PayeeName = table.Column<string>(type: "varchar(100)", nullable: false),
                    Biller = table.Column<string>(type: "varchar(40)", nullable: false),
                    PayerMessage = table.Column<string>(type: "varchar(160)", nullable: false),
                    PayeeNote = table.Column<string>(type: "varchar(160)", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProviderReferenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinancialTransactionId = table.Column<string>(type: "varchar(100)", nullable: false),
                    Receipt = table.Column<string>(type: "varchar(200)", nullable: false),
                    ReasonCode = table.Column<string>(type: "varchar(60)", nullable: false),
                    ReasonMessage = table.Column<string>(type: "varchar(500)", nullable: false),
                    StatusChecks = table.Column<int>(type: "integer", nullable: false),
                    NextStatusCheckAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentOrders_ProviderAccounts_ProviderAccountId",
                        column: x => x.ProviderAccountId,
                        principalSchema: "payments_api",
                        principalTable: "ProviderAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiClients_KeyHash",
                schema: "payments_api",
                table: "ApiClients",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiClients_Name",
                schema: "payments_api",
                table: "ApiClients",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_ClientId_ExternalRef",
                schema: "payments_api",
                table: "PaymentOrders",
                columns: new[] { "ClientId", "ExternalRef" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_ProviderAccountId",
                schema: "payments_api",
                table: "PaymentOrders",
                column: "ProviderAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_ProviderReferenceId",
                schema: "payments_api",
                table: "PaymentOrders",
                column: "ProviderReferenceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_Status_NextStatusCheckAt",
                schema: "payments_api",
                table: "PaymentOrders",
                columns: new[] { "Status", "NextStatusCheckAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderAccounts_OrganisationId_Provider",
                schema: "payments_api",
                table: "ProviderAccounts",
                columns: new[] { "OrganisationId", "Provider" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCalls_PaymentOrderId",
                schema: "payments_api",
                table: "ProviderCalls",
                column: "PaymentOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiClients",
                schema: "payments_api");

            migrationBuilder.DropTable(
                name: "BeneficiaryLookups",
                schema: "payments_api");

            migrationBuilder.DropTable(
                name: "CallbackReceipts",
                schema: "payments_api");

            migrationBuilder.DropTable(
                name: "PaymentOrders",
                schema: "payments_api");

            migrationBuilder.DropTable(
                name: "ProviderCalls",
                schema: "payments_api");

            migrationBuilder.DropTable(
                name: "ProviderAccounts",
                schema: "payments_api");
        }
    }
}
