using BettingAI.Data;
using BettingAI.Models;
using FastEndpoints;

namespace BettingAI.Endpoints;

public class RecordBetRequest
{
    public string? MatchId { get; set; }
    public string? HomeTeam { get; set; }
    public string? AwayTeam { get; set; }
    public string? BetType { get; set; }
    public string? Selection { get; set; }
    public decimal Stake { get; set; }
    public decimal Confidence { get; set; }
    public string? Reasoning { get; set; }
}

public class RecordBetResponse
{
    public int BetId { get; set; }
    public bool Success { get; set; }
}

public class RecordBetEndpoint : Endpoint<RecordBetRequest, RecordBetResponse>
{
    private readonly BettingContext _context;

    public RecordBetEndpoint(BettingContext context)
    {
        _context = context;
    }

    public override void Configure()
    {
        Post("/api/record-bet");
        AllowAnonymous();
    }

    public override async Task HandleAsync(RecordBetRequest req, CancellationToken ct)
    {
        var bet = new Bet
        {
            MatchId = req.MatchId,
            HomeTeam = req.HomeTeam,
            AwayTeam = req.AwayTeam,
            BetType = req.BetType,
            Selection = req.Selection,
            Stake = req.Stake,
            Confidence = req.Confidence,
            Reasoning = req.Reasoning,
            Result = "PENDING"
        };

        _context.Bets.Add(bet);
        await _context.SaveChangesAsync(ct);

        await Send.OkAsync(new RecordBetResponse
        {
            BetId = bet.Id,
            Success = true
        });
    }
}