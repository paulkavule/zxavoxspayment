using zxavoxpaymentbe.Services;

namespace zxavoxpaymentapi.Workers;

/// <summary>Submits waiting payments and runs due status checks (ZD-160, ZD-260), then waits briefly when idle.</summary>
public class PaymentWorker(IServiceScopeFactory scopes, ILogger<PaymentWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var busy = false;
            try
            {
                using var scope = scopes.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<PaymentProcessor>();
                busy = await processor.SubmitNext(stoppingToken) | await processor.CheckNextDue(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "The payment pass failed; retrying shortly");
            }

            if (!busy)
                await Task.Delay(IdleDelay, stoppingToken);
        }
    }
}
