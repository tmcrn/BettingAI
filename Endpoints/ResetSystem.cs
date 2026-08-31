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
        // Clears betting history/portfolio state only. TeamStats is
        // deliberately left alone: seeding it now takes ~8-10 minutes
        // (paced requests across a 2-season window - see
        // TeamStatsSeedingService), and it's refreshed automatically every
        // day anyway, so wiping it here would just force a manual reseed
        // for no benefit - a reset is about the betting history, not stats.
        _context.Bets.RemoveRange(_context.Bets);
        _context.ComboLegs.RemoveRange(_context.ComboLegs);
        _context.BetCombos.RemoveRange(_context.BetCombos);
        _context.MatchContexts.RemoveRange(_context.MatchContexts);
        _context.LearningNotebook.RemoveRange(_context.LearningNotebook);

        await _context.SaveChangesAsync(ct);

        await Send.OkAsync(new ResetSystemResponse
        {
            Success = true,
            Message = "System reset: Portefeuille = 10€, Historique supprimé (TeamStats conservé), Prêt pour vrai test"
        });
    }
}