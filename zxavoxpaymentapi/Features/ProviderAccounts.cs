using FastEndpoints;
using zxavoxpaymentbe.Services;

namespace zxavoxpaymentapi.Features;

public class ListProviderAccountsRequest
{
    [QueryParam]
    public int? OrganisationId { get; set; }
}

public class ListProviderAccounts(ProviderAccountService accounts) : Endpoint<ListProviderAccountsRequest, IReadOnlyList<ProviderAccountView>>
{
    public override void Configure() => Get("/provider-accounts");

    public override async Task HandleAsync(ListProviderAccountsRequest req, CancellationToken ct) =>
        await Send.OkAsync(await accounts.List(req.OrganisationId), ct);
}

public class CreateProviderAccount(ProviderAccountService accounts) : Endpoint<ProviderAccountInput, ProviderAccountView>
{
    public override void Configure() => Post("/provider-accounts");

    public override async Task HandleAsync(ProviderAccountInput req, CancellationToken ct)
    {
        var (account, error) = await accounts.Create(req);
        await Send.ResultAsync(account is null ? Results.Problem(error, statusCode: 422) : Results.Ok(account));
    }
}

public class UpdateProviderAccount(ProviderAccountService accounts) : Endpoint<ProviderAccountInput, ProviderAccountView>
{
    public override void Configure() => Put("/provider-accounts/{id:int}");

    public override async Task HandleAsync(ProviderAccountInput req, CancellationToken ct)
    {
        var (account, error) = await accounts.Update(Route<int>("id"), req);
        await Send.ResultAsync(account is not null ? Results.Ok(account)
            : error is null ? Results.NotFound() : Results.Problem(error, statusCode: 422));
    }
}

/// <summary>Proves an account's credentials by fetching a token; moves no money.</summary>
public class TestProviderAccount(zxavoxpaymentbe.PaymentsDbContext db, ProviderAccountService accounts, IEnumerable<zxavoxpaymentbe.Providers.IPaymentProvider> providers)
    : EndpointWithoutRequest
{
    public override void Configure() => Post("/provider-accounts/{id:int}/test");

    public override async Task HandleAsync(CancellationToken ct)
    {
        var account = await db.ProviderAccounts.FindAsync([Route<int>("id")], ct);
        var provider = providers.FirstOrDefault(p => p.Name == account?.Provider);
        if (account is null || provider is null)
        {
            await Send.ResultAsync(Results.NotFound());
            return;
        }
        var error = await provider.Test(new(account, accounts.Secrets(account), (string)HttpContext.Items["CorrelationId"]!, null), ct);
        await Send.OkAsync(new { ok = error is null, error }, ct);
    }
}
