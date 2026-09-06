using BettingAI.Data;
using BettingAI.Services;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Endpoints;

public class SetScoreRequest
{
    public string Kind { get; set; } = ""; // "bet" | "leg"
    public int Id { get; set; }
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
}

public class SetScoreResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

// Backfills the real match score on a ticket that was already settled
// before HomeScore/AwayScore existed - unlike SetOddsEndpoint this is
// allowed on WIN/LOSS tickets too (that's the whole point: fill in
// history), but it deliberately only ever touches the score fields.
// Result, Winnings, training and odds-learning were already correctly
// applied at settlement time and stay untouched here.
public class SetScoreEndpoint : Endpoint<SetScoreRequest, SetScoreResponse>
{
    private readonly BettingContext _context;

    public SetScoreEndpoint(BettingContext context)
    {
        _context = context;
    }

    public override void Configure()
    {
        Post("/api/set-score");
        AllowAnonymous();
    }

    public override async Task HandleAsync(SetScoreRequest req, CancellationToken ct)
    {
        if (!OwnerAuth.IsAuthorized(HttpContext))
        {
            HttpContext.Response.StatusCode = 403;
            return;
        }

        if (req.HomeScore < 0 || req.AwayScore < 0)
        {
            await Send.OkAsync(new SetScoreResponse { Success = false, Message = "❌ Le score doit être >= 0" });
            return;
        }

        if (req.Kind == "bet")
        {
            var bet = await _context.Bets.FirstOrDefaultAsync(b => b.Id == req.Id, ct);
            if (bet == null)
            {
                await Send.OkAsync(new SetScoreResponse { Success = false, Message = "❌ Pari introuvable" });
                return;
            }

            bet.HomeScore = req.HomeScore;
            bet.AwayScore = req.AwayScore;
            await _context.SaveChangesAsync(ct);
            await Send.OkAsync(new SetScoreResponse { Success = true, Message = $"✅ Score enregistré: {req.HomeScore}-{req.AwayScore}" });
            return;
        }

        if (req.Kind == "leg")
        {
            var leg = await _context.ComboLegs.FirstOrDefaultAsync(l => l.Id == req.Id, ct);
            if (leg == null)
            {
                await Send.OkAsync(new SetScoreResponse { Success = false, Message = "❌ Jambe de combiné introuvable" });
                return;
            }

            leg.HomeScore = req.HomeScore;
            leg.AwayScore = req.AwayScore;
            await _context.SaveChangesAsync(ct);
            await Send.OkAsync(new SetScoreResponse { Success = true, Message = $"✅ Score enregistré: {req.HomeScore}-{req.AwayScore}" });
            return;
        }

        await Send.OkAsync(new SetScoreResponse { Success = false, Message = $"❌ Type inconnu: '{req.Kind}' (attendu 'bet' ou 'leg')" });
    }
}
