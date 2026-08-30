using BettingAI.Services;
using FastEndpoints;

namespace BettingAI.Endpoints;

public class SettlePendingBetsResponse
{
    public bool Success { get; set; }
    public int BetsSettled { get; set; }
    public string? Message { get; set; }
}

// Manual trigger for the same settlement logic AutoSettlementBackgroundService
// runs every 15 minutes - useful for testing or for an external cron that
// wants to force a check right after a match should have ended.
public class SettlePendingBetsEndpoint : EndpointWithoutRequest<SettlePendingBetsResponse>
{
    private readonly BetSettlementService _settlementService;

    public SettlePendingBetsEndpoint(BetSettlementService settlementService)
    {
        _settlementService = settlementService;
    }

    public override void Configure()
    {
        Post("/api/settle-pending-bets");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var settled = await _settlementService.SettlePendingBetsAsync(ct);

        await Send.OkAsync(new SettlePendingBetsResponse
        {
            Success = true,
            BetsSettled = settled,
            Message = settled > 0
                ? $"✅ {settled} pari(s) résolu(s), LearningNotebook mis à jour"
                : "Aucun pari à résoudre pour le moment (matchs pas encore terminés ou déjà réglés)"
        });
    }
}
