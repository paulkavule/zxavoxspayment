using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using zxavoxpaymentbe.Entities;

namespace zxavoxpaymentbe.Messaging;

/// <summary>An event as a handler sees it: identity and context from the message properties, body as JSON text.</summary>
public record ReceivedMessage(Guid EventId, string Type, int OrganisationId, string? CorrelationId, string TransactionId, string Body);

/// <summary>Applies one event. Changes are staged on the scope's DbContext; the inbox commits them.</summary>
public interface IMessageHandler
{
    Task HandleAsync(ReceivedMessage message, CancellationToken ct);
}

/// <summary>Thrown by a handler for a message that can never succeed; it is dead-lettered without retries.</summary>
public class NonRecoverableMessageException(string message) : Exception(message);

/// <summary>
/// Which queue a consumer reads, what it is bound to, the name its inbox rows carry, and how long it
/// waits before each retry (ZD-139). Once the delays are used up the message is dead-lettered.
/// </summary>
public record ConsumerDefinition(string Name, string Queue, string Exchange, IReadOnlyList<string> Bindings)
{
    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } = [TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5)];
}

/// <summary>
/// Reads a durable queue and hands each delivery to THandler through the inbox (ZD-319), so a
/// redelivered event is acknowledged without being applied again. A failure is retried through
/// per-attempt delay queues whose expiry routes back here; when the delays run out, or the message can
/// never succeed, it goes to the dead-letter queue and a DeadLetterItem row (ZD-139). The original is
/// acked only after the commit or after its retry or dead-letter copy is confirmed.
/// </summary>
public sealed class QueueConsumer<THandler>(
    RabbitMqOptions options, IServiceScopeFactory scopes, ConsumerDefinition definition, ILogger<QueueConsumer<THandler>> logger)
    : IAsyncDisposable where THandler : IMessageHandler
{
    private IConnection? connection;
    private IChannel? channel;

    public async Task StartAsync(CancellationToken ct)
    {
        connection = await options.Factory().CreateConnectionAsync(ct);
        // Confirms so a retry or dead-letter copy is safely stored before the original is acked.
        channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true), ct);
        await channel.ExchangeDeclareAsync(definition.Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync(definition.Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        foreach (var binding in definition.Bindings)
            await channel.QueueBindAsync(definition.Queue, definition.Exchange, binding, cancellationToken: ct);
        for (var attempt = 0; attempt < definition.RetryDelays.Count; attempt++)
            await channel.QueueDeclareAsync(MessagingNames.RetryQueue(definition.Queue, attempt), durable: true, exclusive: false, autoDelete: false,
                new Dictionary<string, object?>
                {
                    ["x-message-ttl"] = (long)definition.RetryDelays[attempt].TotalMilliseconds,
                    ["x-dead-letter-exchange"] = "",
                    ["x-dead-letter-routing-key"] = definition.Queue,
                }, cancellationToken: ct);
        await channel.QueueDeclareAsync(MessagingNames.DeadLetterQueue(definition.Queue), durable: true, exclusive: false, autoDelete: false,
            cancellationToken: ct);
        await channel.BasicQosAsync(0, prefetchCount: 10, global: false, ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => Deliver(delivery, ct);
        await channel.BasicConsumeAsync(definition.Queue, autoAck: false, consumer, ct);
    }

    private async Task Deliver(BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        var attempt = Attempt(delivery);
        try
        {
            var message = Read(delivery);
            using var scope = scopes.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<THandler>();
            var applied = await scope.ServiceProvider.GetRequiredService<IInbox>()
                .ProcessOnce(message.EventId, definition.Name, () => handler.HandleAsync(message, ct), ct);
            if (!applied)
                logger.LogInformation("{Consumer} skipped duplicate event {EventId}", definition.Name, message.EventId);
            await channel!.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try
            {
                var permanent = ex is NonRecoverableMessageException or FormatException or System.Text.Json.JsonException;
                if (!permanent && attempt < definition.RetryDelays.Count)
                {
                    logger.LogWarning(ex, "{Consumer} failed attempt {Attempt}; retrying after {Delay}", definition.Name, attempt + 1, definition.RetryDelays[attempt]);
                    await Republish(delivery, MessagingNames.RetryQueue(definition.Queue, attempt), attempt + 1, ex.Message, ct);
                }
                else
                {
                    logger.LogError(ex, "{Consumer} dead-lettered a message after {Attempts} attempt(s)", definition.Name, attempt + 1);
                    await Republish(delivery, MessagingNames.DeadLetterQueue(definition.Queue), attempt + 1, ex.Message, ct);
                    await Record(delivery, attempt + 1, ex.Message, ct);
                }
                await channel!.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
            }
            catch (Exception parking) when (parking is not OperationCanceledException)
            {
                // Neither copy could be stored: hand the message back rather than lose it.
                logger.LogError(parking, "{Consumer} could not park a failed message; returning it to the queue", definition.Name);
                await channel!.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, ct);
            }
        }
    }

    private static int Attempt(BasicDeliverEventArgs delivery) =>
        delivery.BasicProperties.Headers?.TryGetValue(MessagingNames.AttemptHeader, out var value) == true && value is int attempt ? attempt : 0;

    /// <summary>Sends a copy, same id and headers plus the attempt count and failure, straight to a queue.</summary>
    private async Task Republish(BasicDeliverEventArgs delivery, string queue, int attempts, string reason, CancellationToken ct)
    {
        var properties = new BasicProperties(delivery.BasicProperties)
        {
            Persistent = true,
            Headers = new Dictionary<string, object?>(delivery.BasicProperties.Headers ?? new Dictionary<string, object?>())
            {
                [MessagingNames.AttemptHeader] = attempts,
                [MessagingNames.FailureHeader] = reason.Length > 500 ? reason[..500] : reason,
            },
        };
        await channel!.BasicPublishAsync("", queue, mandatory: false, properties, delivery.Body, ct);
    }

    /// <summary>Keeps the dead-lettered message in the database for the operations screens.</summary>
    private async Task Record(BasicDeliverEventArgs delivery, int attempts, string reason, CancellationToken ct)
    {
        var properties = delivery.BasicProperties;
        var headers = properties.Headers ?? new Dictionary<string, object?>();
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        db.DeadLetterItems.Add(new DeadLetterItem
        {
            OrganisationId = int.TryParse(Header(headers, MessagingNames.OrganisationHeader), out var org) ? org : 0,
            EventId = Guid.TryParse(properties.MessageId, out var eventId) ? eventId : null,
            Consumer = definition.Name, Queue = definition.Queue, Type = properties.Type ?? string.Empty,
            TransactionId = Header(headers, MessagingNames.TransactionHeader) ?? string.Empty,
            CorrelationId = properties.CorrelationId ?? string.Empty,
            Payload = Encoding.UTF8.GetString(delivery.Body.Span), Reason = reason.Length > 1000 ? reason[..1000] : reason,
            Attempts = attempts, DeadLetteredAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    private static string? Header(IDictionary<string, object?> headers, string name) =>
        headers.TryGetValue(name, out var value) && value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : value?.ToString();

    private static ReceivedMessage Read(BasicDeliverEventArgs delivery)
    {
        var properties = delivery.BasicProperties;
        var headers = properties.Headers ?? new Dictionary<string, object?>();

        return new ReceivedMessage(
            Guid.Parse(properties.MessageId ?? throw new FormatException("The message has no MessageId (event id)")),
            properties.Type ?? string.Empty,
            int.TryParse(Header(headers, MessagingNames.OrganisationHeader), out var org) ? org : 0,
            properties.CorrelationId,
            Header(headers, MessagingNames.TransactionHeader) ?? string.Empty,
            Encoding.UTF8.GetString(delivery.Body.Span));
    }

    public async ValueTask DisposeAsync()
    {
        if (channel is not null) await channel.DisposeAsync();
        if (connection is not null) await connection.DisposeAsync();
    }
}

/// <summary>
/// Runs a QueueConsumer for the life of the host. Start-up is retried until the broker is reachable;
/// after that the client's automatic recovery restores a dropped connection and its consumer.
/// </summary>
public sealed class QueueConsumerHost<THandler>(
    RabbitMqOptions options, IServiceScopeFactory scopes, ConsumerDefinition definition, ILoggerFactory loggers)
    : BackgroundService where THandler : IMessageHandler
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var logger = loggers.CreateLogger<QueueConsumerHost<THandler>>();
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var consumer = new QueueConsumer<THandler>(options, scopes, definition, loggers.CreateLogger<QueueConsumer<THandler>>());
            try
            {
                await consumer.StartAsync(stoppingToken);
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "{Consumer} could not start; retrying in 10 seconds", definition.Name);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
