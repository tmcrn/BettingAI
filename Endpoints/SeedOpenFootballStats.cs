using BettingAI.Services;
using FastEndpoints;

namespace BettingAI.Endpoints;

public class SeedOpenFootballStatsRequest
{
    [QueryParam]
    // e.g. "2026-27", or a comma-separated list "2026-27,2025-26,2024-25" -
    // defaults to the current season + the 2 previous ones when omitted.
    public string? Season { get; set; }
}

public class SeedOpenFootballStatsResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int TeamsUpdated { get; set; }
}

// Alternative/manual multi-season backfill from openfootball/football.json
// (public domain, no API key). NOT used by the automatic daily refresh -
// TeamStatsRefreshBackgroundService uses football-data.org instead, whose
// team-name spelling is the one every live lookup (AnalyzeMatch, upcoming
// matches) is keyed against. This endpoint is here as a fallback if
// football-data.org is ever unavailable; running it will overwrite
// TeamStats until the next automatic football-data.org refresh takes back
// over (each source clears the table before writing its own numbers).
public class SeedOpenFootballStatsEndpoint : Endpoint<SeedOpenFootballStatsRequest, SeedOpenFootballStatsResponse>
{
    private readonly TeamStatsSeedingService _seedingService;

    public SeedOpenFootballStatsEndpoint(TeamStatsSeedingService seedingService)
    {
        _seedingService = seedingService;
    }

    public override void Configure()
    {
        Post("/api/seed-openfootball-stats");
        AllowAnonymous();
    }

    public override async Task HandleAsync(SeedOpenFootballStatsRequest req, CancellationToken ct)
    {
        if (!OwnerAuth.IsAuthorized(HttpContext))
        {
            HttpContext.Response.StatusCode = 403;
            return;
        }

        var (success, message, teamsUpdated) = await _seedingService.SeedFromOpenFootballAsync(req.Season, ct);

        await Send.OkAsync(new SeedOpenFootballStatsResponse
        {
            Success = success,
            Message = success ? $"✅ {message}" : $"❌ {message}",
            TeamsUpdated = teamsUpdated
        });
    }
}
