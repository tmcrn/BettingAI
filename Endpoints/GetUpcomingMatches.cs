using BettingAI.Services;
using BettingAI.Models;
using FastEndpoints;

namespace BettingAI.Endpoints;

public class GetUpcomingMatchesEndpoint : EndpointWithoutRequest<List<FootballMatch>>
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

    public override async Task HandleAsync(CancellationToken ct)
    {
        var matches = await _footballDataService.GetUpcomingMatchesAsync();
        await Send.OkAsync(matches);
    }
}