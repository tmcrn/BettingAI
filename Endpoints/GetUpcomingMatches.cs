using BettingAI.Services;
using BettingAI.Models;
using FastEndpoints;

namespace BettingAI.Endpoints;

public class GetUpcomingMatchesRequest
{
    [QueryParam]
    public int? WindowHours { get; set; }

    // Start of the window in hours from now (default 0 = "now"). Set to
    // e.g. 72 with WindowHours=96 to fetch only matches between 72h and
    // 96h from now.
    [QueryParam]
    public int? MinHours { get; set; }
}

public class GetUpcomingMatchesEndpoint : Endpoint<GetUpcomingMatchesRequest, List<FootballMatch>>
{
    private readonly FootballDataService _footballDataService;

    public GetUpcomingMatchesEndpoint(FootballDataService footballDataService)
    {
        _footballDataService = footballDataService;
    }

    public override void Configure()
    {
        Get("/api/matches/upcoming");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetUpcomingMatchesRequest req, CancellationToken ct)
    {
        var matches = await _footballDataService.GetUpcomingMatchesAsync(req.WindowHours ?? 24, req.MinHours ?? 0);
        await Send.OkAsync(matches);
    }
}
