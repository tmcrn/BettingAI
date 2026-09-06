using BettingAI.Services;
using FastEndpoints;

namespace BettingAI.Endpoints;

public class SeedRealTeamStatsResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int TeamsUpdated { get; set; }
}

// Manual trigger for TeamStatsSeedingService - same logic
// TeamStatsRefreshBackgroundService runs daily. Useful to force a refresh
// right now instead of waiting for the next automatic cycle.
public class SeedRealTeamStatsEndpoint : EndpointWithoutRequest<SeedRealTeamStatsResponse>
{
    private readonly TeamStatsSeedingService _seedingService;

    public SeedRealTeamStatsEndpoint(TeamStatsSeedingService seedingService)
    {
        _seedingService = seedingService;
    }

    public override void Configure()
    {
        Post("/api/seed-real-team-stats");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!OwnerAuth.IsAuthorized(HttpContext))
        {
            HttpContext.Response.StatusCode = 403;
            return;
        }

        var (success, message, teamsUpdated) = await _seedingService.SeedFromRealMatchesAsync(ct);

        await Send.OkAsync(new SeedRealTeamStatsResponse
        {
            Success = success,
            Message = success ? $"✅ {message}" : $"❌ {message}",
            TeamsUpdated = teamsUpdated
        });
    }
}
