using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using zxavoxpaymentbe;
using zxavoxpaymentbe.Entities;
using zxavoxpaymentbe.Messaging;
using zxavoxpaymentbe.Services;

namespace zxavoxpaymentapi.Health;

// Liveness and readiness of the payment service (ZD-296). Checks report counts and ages only; a failure's
// description is a fixed sentence and its exception is logged, so a probe never shows keys or connection strings.

public static class PaymentHealth
{
    public const string Ready = "ready";

    public static IHealthChecksBuilder AddPaymentChecks(this IServiceCollection services) => services.AddHealthChecks()
        .AddCheck<DatabaseHealthCheck>("database", tags: [Ready])
        .AddCheck<OutboxHealthCheck>("outbox", tags: [Ready])
        .AddCheck<DeadLetterHealthCheck>("dead-letters", tags: [Ready])
        .AddCheck<ProviderHealthCheck>("providers", tags: [Ready])
        .AddCheck<StuckPaymentHealthCheck>("stuck-payments", tags: [Ready])
        .AddTypeActivatedCheck<RabbitMqHealthCheck>("rabbitmq", null, [Ready], PaymentRequestHandler.Definition.Queue);

    public static void MapHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = Write }).AllowAnonymous();
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(Ready), ResponseWriter = Write })
            .AllowAnonymous();
    }

    private static Task Write(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            durationMs = (int)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(e => e.Key, e => new
            {
                status = e.Value.Status.ToString(), description = e.Value.Description, durationMs = (int)e.Value.Duration.TotalMilliseconds,
                data = e.Value.Data,
            }),
        }));
    }
}

public class DatabaseHealthCheck(PaymentsDbContext db, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(ct)
                ? HealthCheckResult.Healthy("The database answers")
                : HealthCheckResult.Unhealthy("The database cannot be reached");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Database health check failed");
            return HealthCheckResult.Unhealthy("The database cannot be reached");
        }
    }
}

/// <summary>Outcome events waiting to reach the broker, and the oldest one's age.</summary>
public class OutboxHealthCheck(PaymentsDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var waiting = db.OutboxMessages.AsNoTracking().Where(m => m.PublishedAt == null);
        var backlog = await waiting.CountAsync(ct);
        var oldest = await waiting.MinAsync(m => (DateTime?)m.CreatedAt, ct);
        var age = oldest is null ? TimeSpan.Zero : DateTime.UtcNow - oldest.Value;
        var data = new Dictionary<string, object> { ["backlog"] = backlog, ["oldestAgeSeconds"] = (int)age.TotalSeconds };
        if (age > TimeSpan.FromMinutes(30))
            return HealthCheckResult.Unhealthy($"An event has waited {(int)age.TotalMinutes} minutes to be published", data: data);
        return age > TimeSpan.FromMinutes(5)
            ? HealthCheckResult.Degraded($"An event has waited {(int)age.TotalMinutes} minutes to be published", data: data)
            : HealthCheckResult.Healthy($"{backlog} events waiting", data);
    }
}

public class DeadLetterHealthCheck(PaymentsDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var open = await db.DeadLetterItems.CountAsync(d => d.Status == zxadocslib.Dtos.DeadLetterStatus.Open, ct);
        var data = new Dictionary<string, object> { ["open"] = open };
        return open > 0
            ? HealthCheckResult.Degraded($"{open} dead-lettered messages need review", data: data)
            : HealthCheckResult.Healthy("No dead-lettered messages", data);
    }
}

/// <summary>
/// Provider health as far as it can be seen from here: calls per provider in the last 15 minutes and how many
/// failed (no answer, or a 5xx). Degraded when a provider's recent calls all failed.
/// </summary>
public class ProviderHealthCheck(PaymentsDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddMinutes(-15);
        var calls = await db.ProviderCalls.AsNoTracking().Where(c => c.At >= since)
            .GroupBy(c => c.Provider)
            .Select(g => new { Provider = g.Key, Calls = g.Count(), Failed = g.Count(c => c.HttpStatus == null || c.HttpStatus >= 500) })
            .ToListAsync(ct);
        var data = calls.ToDictionary(c => c.Provider, c => (object)new { calls = c.Calls, failed = c.Failed });
        var down = calls.Where(c => c.Calls >= 3 && c.Failed == c.Calls).Select(c => c.Provider).ToList();
        return down.Count > 0
            ? HealthCheckResult.Degraded($"Every recent call to {string.Join(", ", down)} failed", data: data)
            : HealthCheckResult.Healthy("No provider is failing every call", data);
    }
}

/// <summary>Payments still unresolved after the polling cutoff, which only reconciliation will settle.</summary>
public class StuckPaymentHealthCheck(PaymentsDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var before = DateTime.UtcNow - PaymentProcessor.Cutoff;
        var stuck = await db.PaymentOrders.CountAsync(o => o.CreatedAt < before
            && (o.Status == PaymentOrderStatus.Pending || o.Status == PaymentOrderStatus.PendingUnknown), ct);
        var data = new Dictionary<string, object> { ["stuck"] = stuck, ["cutoffHours"] = (int)PaymentProcessor.Cutoff.TotalHours };
        return stuck > 0
            ? HealthCheckResult.Degraded($"{stuck} payments are still unresolved after the cutoff", data: data)
            : HealthCheckResult.Healthy("No payment is past the cutoff", data);
    }
}

/// <summary>The broker answers and the payment request queue and its dead-letter queue exist; reports depth and consumers.</summary>
public class RabbitMqHealthCheck(RabbitMqOptions options, string queue, ILogger<RabbitMqHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var data = new Dictionary<string, object>();
        try
        {
            var factory = options.Factory();
            factory.RequestedConnectionTimeout = TimeSpan.FromSeconds(3);
            await using var connection = await factory.CreateConnectionAsync(ct);
            var degraded = false;
            foreach (var name in new[] { queue, MessagingNames.DeadLetterQueue(queue) })
            {
                // A passive declare that fails closes its channel, so each queue gets its own.
                await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
                var ok = await channel.QueueDeclarePassiveAsync(name, ct);
                data[name] = new { messages = ok.MessageCount, consumers = ok.ConsumerCount };
                degraded |= name == queue ? ok.MessageCount > 1_000 : ok.MessageCount > 0;
            }
            return degraded
                ? HealthCheckResult.Degraded("A queue is backing up or holds dead letters", data: data)
                : HealthCheckResult.Healthy("The broker answers and every queue exists", data);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "RabbitMQ health check failed");
            return HealthCheckResult.Unhealthy("The message broker or one of its queues cannot be reached", data: data);
        }
    }
}
