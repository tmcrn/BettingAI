using BettingAI.Services;
using FastEndpoints;

namespace BettingAI.Endpoints;

public class ManualSettleRequest
{
    public string Kind { get; set; } = ""; // "bet" | "leg"
    public int Id { get; set; }
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    public decimal? RealOdds { get; set; } // optional - only when none were resolved automatically
}

public class ManualSettleResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

// Manual fallback for a bet/combo leg stuck PENDING more than 3h after
// kickoff - the user reports the real final score (and, if it was never
// resolved automatically, the real odds) instead of the system guessing
// or leaving it PENDING forever.
public class ManualSettleEndpoint : Endpoint<ManualSettleRequest, ManualSettleResponse>
{
    private readonly BetSettlementService _settlementService;

    public ManualSettleEndpoint(BetSettlementService settlementService)
    {
        _settlementService = settlementService;
    }

    public override void Configure()
    {
        Post("/api/manual-settle");
        AllowAnonymous();
    }

    public override async Task HandleAsync(ManualSettleRequest req, CancellationToken ct)
    {
        var (success, message) = req.Kind switch
        {
            "bet" => await _settlementService.ManualSettleBetAsync(req.Id, req.HomeScore, req.AwayScore, req.RealOdds, ct),
            "leg" => await _settlementService.ManualSettleLegAsync(req.Id, req.HomeScore, req.AwayScore, req.RealOdds, ct),
            _ => (false, $"Type inconnu: '{req.Kind}' (attendu 'bet' ou 'leg')")
        };

        await Send.OkAsync(new ManualSettleResponse
        {
            Success = success,
            Message = success ? $"✅ {message}" : $"❌ {message}"
        });
    }
}
