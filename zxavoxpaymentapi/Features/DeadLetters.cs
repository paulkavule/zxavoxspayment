using System.Security.Claims;
using FastEndpoints;
using zxadocslib.Dtos;
using zxavoxpaymentbe.Entities;
using zxavoxpaymentbe.Messaging;

namespace zxavoxpaymentapi.Features;

public class ListDeadLettersRequest
{
    [QueryParam]
    public DeadLetterStatus? Status { get; set; }
}

public class ListDeadLetters(DeadLetterService deadLetters) : Endpoint<ListDeadLettersRequest, IReadOnlyList<DeadLetterItem>>
{
    public override void Configure() => Get("/dead-letters");

    public override async Task HandleAsync(ListDeadLettersRequest req, CancellationToken ct) =>
        await Send.OkAsync(await deadLetters.List(req.Status), ct);
}

public class ReplayDeadLetter(DeadLetterService deadLetters) : EndpointWithoutRequest<DeadLetterItem>
{
    public override void Configure() => Post("/dead-letters/{id:int}/replay");

    public override async Task HandleAsync(CancellationToken ct)
    {
        var (item, error) = await deadLetters.Replay(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), Route<int>("id"));
        await Send.ResultAsync(item is null ? Results.NotFound() : error is null ? Results.Ok(item) : Results.Problem(error, statusCode: 409));
    }
}
