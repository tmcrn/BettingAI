using FastEndpoints;
using BettingAI.Services;

namespace BettingAI.Endpoints;

public class FetchOddsRequest
{
    public string? HomeTeam { get; set; }
    public string? AwayTeam { get; set; }
}

public class OddData
{
    // Real 1X2 odds scraped from Sofascore - the only market we have
    // verified real pricing for. Goal markets (BTTS, over/under) used to be
    // hardcoded fake numbers here (1.85/1.75/2.05, always, never real) -
    // removed rather than fabricated. The AI reasons about those markets
    // from xG stats confidence alone now, not a fake EV.
    public decimal HomeWin { get; set; }
    public decimal Draw { get; set; }
    public decimal AwayWin { get; set; }
}

public class FetchOddsResponse
{
    public bool Success { get; set; }
    public OddData? Odds { get; set; }
    public string? Message { get; set; }
}

public class FetchOddsEndpoint : Endpoint<FetchOddsRequest, FetchOddsResponse>
{
    private readonly OddsScraperService _scraper;

    public FetchOddsEndpoint(OddsScraperService scraper)
    {
        _scraper = scraper;
    }

    public override void Configure()
    {
        Post("/api/fetch-odds");
        AllowAnonymous();
    }

    public override async Task HandleAsync(FetchOddsRequest req, CancellationToken ct)
    {
        try
        {
            var oddsDict = await _scraper.GetSofascoreOdds(req.HomeTeam, req.AwayTeam);

            if (oddsDict == null)
            {
                await Send.OkAsync(new FetchOddsResponse
                {
                    Success = false,
                    Message = "Failed to scrape odds"
                });
                return;
            }

            var odds = new OddData
            {
                HomeWin = oddsDict["homeWin"],
                Draw = oddsDict["draw"],
                AwayWin = oddsDict["awayWin"]
            };

            await Send.OkAsync(new FetchOddsResponse
            {
                Success = true,
                Odds = odds,
                Message = "✅ Real odds scraped from Sofascore"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            await Send.OkAsync(new FetchOddsResponse
            {
                Success = false,
                Message = ex.Message
            });
        }
    }
}