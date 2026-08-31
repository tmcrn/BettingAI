using BettingAI.Data;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Endpoints;

public class ResetSystemResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public class ResetSystemEndpoint : EndpointWithoutRequest<ResetSystemResponse>
{
    private readonly BettingContext _context;

    public ResetSystemEndpoint(BettingContext context)
    {
        _context = context;
    }

    public override void Configure()
    {
        Delete("/api/reset-system");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // Clear ALL data
        _context.Bets.RemoveRange(_context.Bets);
        _context.ComboLegs.RemoveRange(_context.ComboLegs);
        _context.BetCombos.RemoveRange(_context.BetCombos);
        _context.TeamStats.RemoveRange(_context.TeamStats);
        _context.MatchContexts.RemoveRange(_context.MatchContexts);
        _context.LearningNotebook.RemoveRange(_context.LearningNotebook);

        await _context.SaveChangesAsync(ct);

        await Send.OkAsync(new ResetSystemResponse
        {
            Success = true,
            Message = "System reset: Portefeuille = 10€, Historique supprimé, Prêt pour vrai test"
        });
    }
}