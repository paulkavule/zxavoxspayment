using zxavoxpaymentbe.Messaging;

namespace zxavoxpaymentapi.Workers;

/// <summary>Drains the outbox (ZD-298, payment service): publishes due events in batches, then waits briefly when idle.</summary>
public class OutboxPublisherWorker(IServiceScopeFactory scopes, ILogger<OutboxPublisherWorker> logger) : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var published = 0;
            try
            {
                using var scope = scopes.CreateScope();
                published = await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().PublishDueAsync(BatchSize, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "The outbox pass failed; retrying shortly");
            }

            // A full batch means more may be waiting, so go again straight away.
            if (published < BatchSize)
                await Task.Delay(IdleDelay, stoppingToken);
        }
    }
}
