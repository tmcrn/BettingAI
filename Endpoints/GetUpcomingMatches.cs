using BettingAI.Services;
using BettingAI.Models;
using FastEndpoints;

namespace BettingAI.Endpoints;

public class GetUpcomingMatchesRequest
{
    [QueryParam]
    public int? WindowHours { get; set; }
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
        var matches = await _footballDataService.GetUpcomingMatchesAsync(req.WindowHours ?? 24);
        await Send.OkAsync(matches);
    }
}
