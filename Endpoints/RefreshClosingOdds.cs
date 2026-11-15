using BettingAI.Services;
using FastEndpoints;

namespace BettingAI.Endpoints;

public class RefreshClosingOddsRequest
{
    // Only leagues with a pending bet kicking off within this many hours
    // are re-fetched (2 credits per league) - default 3h.
    [QueryParam]
    public int? WithinHours { get; set; }
}

public class RefreshClosingOddsResponse
{
    public bool Success { get; set; }
    public int LeaguesFetched { get; set; }
    public int BetsUpdated { get; set; }
    public string? Message { get; set; }
}

// Records the "closing line" of pending bets: the bookmaker's price shortly
// before kickoff. Call it a little before matches start (dashboard button,
// or a scheduled task) - the closer to kickoff, the better the CLV figure.
public class RefreshClosingOddsEndpoint : Endpoint<RefreshClosingOddsRequest, RefreshClosingOddsResponse>
{
    private readonly ClosingLineService _closingLine;

    public RefreshClosingOddsEndpoint(ClosingLineService closingLine)
    {
        _closingLine = closingLine;
    }

    public override void Configure()
    {
        Post("/api/refresh-closing-odds");
        AllowAnonymous();
    }

    public override async Task HandleAsync(RefreshClosingOddsRequest req, CancellationToken ct)
    {
        if (!OwnerAuth.IsAuthorized(HttpContext))
        {
            HttpContext.Response.StatusCode = 403;
            return;
        }

        if (!OddsApiService.IsConfigured)
        {
            await Send.OkAsync(new RefreshClosingOddsResponse { Success = false, Message = "THE_ODDS_API_KEY non configurée dans .env" });
            return;
        }

        try
        {
            var (leagues, updated) = await _closingLine.RefreshAsync(Math.Clamp(req.WithinHours ?? 3, 1, 72), ct);
            await Send.OkAsync(new RefreshClosingOddsResponse
            {
                Success = true,
                LeaguesFetched = leagues,
                BetsUpdated = updated,
                Message = leagues == 0
                    ? "Aucun pari en attente ne commence bientôt - rien à rafraîchir (0 crédit utilisé)"
                    : $"✅ Cote de clôture mise à jour sur {updated} pari(s) ({leagues} championnat(s), {leagues * 2} crédits). {OddsApiService.LastQuotaInfo}"
            });
        }
        catch (Exception ex)
        {
            await Send.OkAsync(new RefreshClosingOddsResponse { Success = false, Message = $"❌ {ex.Message}" });
        }
    }
}
