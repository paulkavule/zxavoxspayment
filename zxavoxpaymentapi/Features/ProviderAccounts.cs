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
