using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using zxavoxpaymentbe;
using zxavoxpaymentbe.Security;

namespace zxavoxpaymentapi.Tests;

/// <summary>The API on a real Postgres with the migrations applied, and one bootstrapped client key.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string ClientKey = "test-client-key-0123456789abcdef";
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> factory = default!;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("DB_HOST", connection.Host);
        Environment.SetEnvironmentVariable("DB_PORT", connection.Port.ToString());
        Environment.SetEnvironmentVariable("DB_NAME", connection.Database);
        Environment.SetEnvironmentVariable("DB_UNAME", connection.Username);
        Environment.SetEnvironmentVariable("DB_PWORD", connection.Password);
        Environment.SetEnvironmentVariable(SecretCipher.KeyVariable, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Environment.SetEnvironmentVariable("PAY_BOOTSTRAP_CLIENT", "zxadocs");
        Environment.SetEnvironmentVariable("PAY_BOOTSTRAP_API_KEY", ClientKey);

        await using (var db = NewDb())
            await db.Database.MigrateAsync();
        factory = new WebApplicationFactory<Program>();
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        await postgres.DisposeAsync();
    }

    public HttpClient Client(string? key = ClientKey)
    {
        var client = factory.CreateClient();
        if (key is not null)
            client.DefaultRequestHeaders.Add(ApiKeys.Header, key);
        return client;
    }

    public PaymentsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<PaymentsDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "Api";
}
