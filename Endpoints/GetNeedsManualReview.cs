using BettingAI.Services;
using FastEndpoints;

namespace BettingAI.Endpoints;

public class GetNeedsManualReviewResponse
{
    public List<ManualReviewItem> Items { get; set; } = new();
}

// Bets/combo legs whose match kicked off more than 3h ago and are still
// PENDING - auto-settlement has had its chance and never got a real
// result, so the dashboard surfaces these for manual score/odds entry.
public class GetNeedsManualReviewEndpoint : EndpointWithoutRequest<GetNeedsManualReviewResponse>
{
    private readonly BetSettlementService _settlementService;

    public GetNeedsManualReviewEndpoint(BetSettlementService settlementService)
    {
        _settlementService = settlementService;
    }

    public override void Configure()
    {
        Get("/api/needs-manual-review");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var items = await _settlementService.GetItemsNeedingManualReviewAsync(ct);
        await Send.OkAsync(new GetNeedsManualReviewResponse { Items = items });
    }
}
