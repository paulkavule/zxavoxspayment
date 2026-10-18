using System.Text;
using RabbitMQ.Client;
using zxavoxpaymentbe.Entities;

namespace zxavoxpaymentbe.Messaging;

/// <summary>Publishes to RabbitMQ and returns only once the broker has confirmed the message.</summary>
public interface IMessagePublisher
{
    /// <summary>Throws when the broker cannot be reached or rejects the message (nack).</summary>
    Task PublishAsync(OutboxMessage message, CancellationToken ct);
}

/// <summary>
/// One connection and one confirm-enabled channel for the process (ZD-298), opened on first use and
/// reopened after a failure. Publishes are serialised: a channel is not safe for concurrent use.
/// </summary>
public sealed class RabbitMqPublisher(RabbitMqOptions options) : IMessagePublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private IConnection? connection;
    private IChannel? channel;

    public async Task PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var open = await Channel(ct);
            var properties = new BasicProperties
            {
                Persistent = true, 
                MessageId = message.EventId.ToString(), 
                Type = message.Type, 
                ContentType = "application/json",
                CorrelationId = message.CorrelationId, 
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?>
                {
                    [MessagingNames.CorrelationHeader] = message.CorrelationId,
                    [MessagingNames.OrganisationHeader] = message.OrganisationId,
                    [MessagingNames.TransactionHeader] = message.TransactionId,
                },
            };
            // With confirmation tracking on, this completes on the broker's ack and throws on a nack.
            await open.BasicPublishAsync(message.Exchange, message.RoutingKey, mandatory: false, properties,
                Encoding.UTF8.GetBytes(message.Payload), ct);
        }
        catch
        {
            await Close();
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<IChannel> Channel(CancellationToken ct)
    {
        if (channel is { IsOpen: true })
            return channel;

        await Close();
        connection = await options.Factory().CreateConnectionAsync(ct);
        channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
        await channel.ExchangeDeclareAsync(MessagingNames.PaymentsExchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
        return channel;
    }

    private async Task Close()
    {
        if (channel is not null) await channel.DisposeAsync();
        if (connection is not null) await connection.DisposeAsync();
        channel = null;
        connection = null;
    }

    public async ValueTask DisposeAsync() => await Close();
}
