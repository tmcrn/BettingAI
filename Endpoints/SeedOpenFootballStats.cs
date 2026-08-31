using BettingAI.Services;
using FastEndpoints;

namespace BettingAI.Endpoints;

public class SeedOpenFootballStatsRequest
{
    [QueryParam]
    public string? Season { get; set; } // e.g. "2026-27" - defaults to the current season
}

public class SeedOpenFootballStatsResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int TeamsUpdated { get; set; }
}

// One-time deeper historical backfill from openfootball/football.json
// (public domain, no API key) - covers the whole season so far instead of
// just the last 45 days. The daily football-data.org refresh
// (TeamStatsRefreshBackgroundService) naturally takes back over from here.
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
        var (success, message, teamsUpdated) = await _seedingService.SeedFromOpenFootballAsync(req.Season, ct);

        await Send.OkAsync(new SeedOpenFootballStatsResponse
        {
            Success = success,
            Message = success ? $"✅ {message}" : $"❌ {message}",
            TeamsUpdated = teamsUpdated
        });
    }
}
