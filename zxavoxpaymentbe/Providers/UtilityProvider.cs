using zxavoxpaymentbe.Entities;

namespace zxavoxpaymentbe.Providers;

/// <summary>
/// Utility bills (ZD-201). No biller aggregator is chosen yet, so every payment fails at once with
/// UTILITY_NOT_CONFIGURED and Zxadocs releases the funds. A real biller replaces this class.
/// </summary>
public class UtilityProvider : IPaymentProvider
{
    private const string NotConfigured = "No utility biller is integrated yet";

    public string Name => "UTILITY";

    public Task<ProviderResult> Submit(ProviderContext context, PaymentOrder order, CancellationToken ct) =>
        Task.FromResult(new ProviderResult(ProviderState.Failed, "UTILITY_NOT_CONFIGURED", NotConfigured));

    public Task<ProviderResult> Status(ProviderContext context, PaymentOrder order, CancellationToken ct) =>
        Task.FromResult(new ProviderResult(ProviderState.Unknown));

    public Task<LookupResult> Lookup(ProviderContext context, string msisdn, CancellationToken ct) =>
        Task.FromResult(new LookupResult(false, "", "Utility accounts have no name lookup"));

    public Task<string?> Test(ProviderContext context, CancellationToken ct) => Task.FromResult<string?>(NotConfigured);
}
