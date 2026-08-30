using BettingAI.Data;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Endpoints;

public class RecordMatchResultRequest
{
    public string? MatchId { get; set; }
    public string? Result { get; set; }  // "HOME_WIN", "AWAY_WIN", "DRAW"
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
}

public class RecordMatchResultResponse
{
    public int BetsUpdated { get; set; }
    public decimal TotalWinnings { get; set; }
}

public class RecordMatchResultEndpoint : Endpoint<RecordMatchResultRequest, RecordMatchResultResponse>
{
    private readonly BettingContext _context;

    public RecordMatchResultEndpoint(BettingContext context)
    {
        _context = context;
    }

    public override void Configure()
    {
        Post("/api/record-result");
        AllowAnonymous();
    }

    public override async Task HandleAsync(RecordMatchResultRequest req, CancellationToken ct)
    {
        var bets = await _context.Bets
            .Where(b => b.MatchId == req.MatchId && b.Result == "PENDING")
            .ToListAsync(cancellationToken: ct);

        decimal totalWinnings = 0;

        foreach (var bet in bets)
        {
            var won = false;

            if (bet.BetType == "HOME_WIN" && req.Result == "HOME_WIN")
                won = true;
            else if (bet.BetType == "AWAY_WIN" && req.Result == "AWAY_WIN")
                won = true;
            else if (bet.BetType == "DRAW" && req.Result == "DRAW")
                won = true;
            else if (bet.BetType == "BOTH_TEAMS_SCORE" && req.HomeScore > 0 && req.AwayScore > 0)
                won = true;
            else if (bet.BetType == "OVER_GOALS" && (req.HomeScore + req.AwayScore) > 2.5m)
                won = true;
            else if (bet.BetType == "UNDER_GOALS" && (req.HomeScore + req.AwayScore) < 2.5m)
                won = true;

            if (won)
            {
                bet.Result = "WIN";
                bet.Winnings = bet.Stake * 2;  // Cotes simples : x2
                totalWinnings += bet.Winnings.Value;
            }
            else
            {
                bet.Result = "LOSS";
                bet.Winnings = 0;
            }
        }

        await _context.SaveChangesAsync(ct);

        await Send.OkAsync(new RecordMatchResultResponse
        {
            BetsUpdated = bets.Count,
            TotalWinnings = totalWinnings
        });
    }
}