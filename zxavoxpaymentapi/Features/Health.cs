using FastEndpoints;
using zxavoxpaymentbe;

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

/// <summary>Ready when the database answers. RabbitMQ is added with the messaging step.</summary>
public class Ready(PaymentsDbContext db) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/health/ready");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var database = await db.Database.CanConnectAsync(ct);
        await Send.ResultAsync(Results.Json(new { status = database ? "ready" : "unavailable", database },
            statusCode: database ? 200 : 503));
    }
}
