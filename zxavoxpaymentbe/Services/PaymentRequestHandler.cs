using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using zxadocslib.Dtos;
using zxavoxpaymentbe.Helpers;
using zxavoxpaymentbe.Messaging;

namespace zxavoxpaymentbe.Services;

/// <summary>Consumes payment.requested.# (ZD-160). Orders belong to the client named by PAY_EVENTS_CLIENT (default zxadocs).</summary>
public class PaymentRequestHandler(PaymentsDbContext db, PaymentProcessor processor) : IMessageHandler
{
    public static readonly ConsumerDefinition Definition =
        new("payment-requests", "zxavox.payment-requests", MessagingNames.PaymentsExchange, ["payment.requested.#"]);

    private static readonly string[] Providers = ["MTN", "AIRTEL", "UTILITY"];

    public async Task HandleAsync(ReceivedMessage message, CancellationToken ct)
    {
        var request = JsonSerializer.Deserialize<PaymentRequested>(message.Body)
            ?? throw new NonRecoverableMessageException("Empty payment.requested body");
        if (request.Amount <= 0 || !Providers.Contains(request.Provider) || request.ExternalRef.Length is 0 or > 60)
            throw new NonRecoverableMessageException($"Invalid payment.requested for '{request.ExternalRef}'");

        var name = ServiceEnvironment.Value("PAY_EVENTS_CLIENT", "zxadocs");
        var clientId = await db.ApiClients.Where(c => c.Name == name).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"No API client named '{name}' owns payment events");
        await processor.Accept(clientId, request, ct);
    }
}
