using BettingAI.Services;
using FastEndpoints;

namespace BettingAI.Endpoints;

public class RefreshLiveScoresResponse
{
    public bool Success { get; set; }

    // Tickets (single bets + combos), not rows - see the comment on
    // BetSettlementService.RefreshLiveScoresAsync.
    public int TicketsUpdated { get; set; }
    public string? Message { get; set; }
}

// Purely cosmetic score refresh for matches still in progress. Unlike
// SettlePendingBetsEndpoint this never touches Result/Winnings/training -
// it only updates HomeScore/AwayScore for display, and does so regardless
// of BetSettlementService's 2h post-kickoff buffer (that buffer exists
// precisely to wait until a match SHOULD be over before treating a score
// as final; this is the opposite - a number that keeps changing until the
// real settlement pass takes over). Deliberately NOT behind OwnerAuth,
// unlike every other mutating endpoint - a guest watching a live match is
// exactly the kind of thing a read-only visitor should be able to do, and
// this never touches anything a settlement/reset actually protects.
public class RefreshLiveScoresEndpoint : EndpointWithoutRequest<RefreshLiveScoresResponse>
{
    private readonly BetSettlementService _settlementService;

    public RefreshLiveScoresEndpoint(BetSettlementService settlementService)
    {
        _settlementService = settlementService;
    }

    public override void Configure()
    {
        Post("/api/refresh-live-scores");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var updated = await _settlementService.RefreshLiveScoresAsync(ct);

        await Send.OkAsync(new RefreshLiveScoresResponse
        {
            Success = true,
            TicketsUpdated = updated,
            Message = updated > 0
                ? $"🔴 {updated} ticket(s) en cours mis à jour"
                : "Aucun match en cours parmi les paris en attente"
        });
    }
}
