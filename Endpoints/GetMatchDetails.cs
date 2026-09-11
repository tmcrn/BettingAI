using BettingAI.Data;
using BettingAI.Models;
using BettingAI.Services;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Endpoints;

// Manual "Fiche match" lookup - lets a real bettor pick an upcoming match
// (from GetUpcomingMatchesEndpoint's list) and see the same kind of data
// Robert reasons from before placing a bet themselves, independent of the
// AI cycle: real head-to-head history, each team's recent form, and their
// stored TeamStats if already seeded. Read-only, so unguarded like the
// other GET endpoints (guests can use it too).
public class GetMatchDetailsRequest
{
    // Real football-data.org match id - required for GetHeadToHeadAsync,
    // which is a per-fixture endpoint on their side. Comes straight off
    // the FootballMatch.Id a GetUpcomingMatchesEndpoint call already
    // returned for this fixture.
    [QueryParam]
    public string? MatchId { get; set; }

    [QueryParam]
    public string? HomeTeam { get; set; }

    [QueryParam]
    public string? AwayTeam { get; set; }
}

public class GetMatchDetailsResponse
{
    public HeadToHead? HeadToHead { get; set; }

    // Most recent first - each team's own last results against ANY
    // opponent (not just each other), same table DecideBets reasons from.
    public List<TeamRecentResult> HomeRecentResults { get; set; } = new();
    public List<TeamRecentResult> AwayRecentResults { get; set; } = new();

    // Null when that team hasn't been through TeamStatsSeedingService yet
    // (e.g. a club outside the tracked leagues) - the frontend just omits
    // the section rather than showing zeros.
    public TeamStats? HomeStats { get; set; }
    public TeamStats? AwayStats { get; set; }
}

public class GetMatchDetailsEndpoint : Endpoint<GetMatchDetailsRequest, GetMatchDetailsResponse>
{
    private readonly BettingContext _context;
    private readonly FootballDataService _footballDataService;

    public GetMatchDetailsEndpoint(BettingContext context, FootballDataService footballDataService)
    {
        _context = context;
        _footballDataService = footballDataService;
    }

    public override void Configure()
    {
        Get("/api/match-details");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetMatchDetailsRequest req, CancellationToken ct)
    {
        var response = new GetMatchDetailsResponse();

        if (!string.IsNullOrEmpty(req.MatchId))
        {
            response.HeadToHead = await _footballDataService.GetHeadToHeadAsync(req.MatchId);
        }

        if (!string.IsNullOrEmpty(req.HomeTeam))
        {
            response.HomeRecentResults = await _context.TeamRecentResults
                .Where(r => r.TeamName == req.HomeTeam)
                .OrderByDescending(r => r.MatchDate)
                .Take(10)
                .ToListAsync(ct);
            response.HomeStats = await _context.TeamStats
                .Where(s => s.TeamName == req.HomeTeam)
                .OrderByDescending(s => s.LastUpdated)
                .FirstOrDefaultAsync(ct);
        }

        if (!string.IsNullOrEmpty(req.AwayTeam))
        {
            response.AwayRecentResults = await _context.TeamRecentResults
                .Where(r => r.TeamName == req.AwayTeam)
                .OrderByDescending(r => r.MatchDate)
                .Take(10)
                .ToListAsync(ct);
            response.AwayStats = await _context.TeamStats
                .Where(s => s.TeamName == req.AwayTeam)
                .OrderByDescending(s => s.LastUpdated)
                .FirstOrDefaultAsync(ct);
        }

        await Send.OkAsync(response);
    }
}
