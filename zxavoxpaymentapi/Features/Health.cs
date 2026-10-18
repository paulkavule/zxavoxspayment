using FastEndpoints;
using zxavoxpaymentbe;
using zxavoxpaymentbe.Messaging;

namespace zxavoxpaymentapi.Features;

public class Live : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/health/live");
        AllowAnonymous();
    }

    public override Task HandleAsync(CancellationToken ct) => Send.OkAsync(new { status = "live" }, ct);
}

/// <summary>Ready when both the database and the broker answer.</summary>
public class Ready(PaymentsDbContext db, RabbitMqOptions broker) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/health/ready");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var database = await db.Database.CanConnectAsync(ct);
        var rabbitMq = true;
        try
        {
            var factory = broker.Factory();
            factory.RequestedConnectionTimeout = TimeSpan.FromSeconds(3);
            await using var connection = await factory.CreateConnectionAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            rabbitMq = false;
        }
        var ready = database && rabbitMq;
        await Send.ResultAsync(Results.Json(new { status = ready ? "ready" : "unavailable", database, rabbitMq }, statusCode: ready ? 200 : 503));
    }
}
