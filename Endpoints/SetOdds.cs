using BettingAI.Data;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Endpoints;

public class SetOddsRequest
{
    public string Kind { get; set; } = ""; // "bet" | "leg"
    public int Id { get; set; }
    public decimal Odds { get; set; }
}

public class SetOddsResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

// Lets the user directly enter the real odds they see on their own
// bookmaker for a bet/combo leg, instead of relying only on the Sofascore
// scrape (often not published yet at decision time) or the confidence-
// derived estimate. Only while PENDING - once a bet is settled its payout
// is already computed and fixed.
public class SetOddsEndpoint : Endpoint<SetOddsRequest, SetOddsResponse>
{
    private readonly BettingContext _context;

    public SetOddsEndpoint(BettingContext context)
    {
        _context = context;
    }

    public override void Configure()
    {
        Post("/api/set-odds");
        AllowAnonymous();
    }

    public override async Task HandleAsync(SetOddsRequest req, CancellationToken ct)
    {
        if (req.Odds < 1m)
        {
            await Send.OkAsync(new SetOddsResponse { Success = false, Message = "❌ La cote doit être >= 1.0" });
            return;
        }

        if (req.Kind == "bet")
        {
            var bet = await _context.Bets.FirstOrDefaultAsync(b => b.Id == req.Id, ct);
            if (bet == null)
            {
                await Send.OkAsync(new SetOddsResponse { Success = false, Message = "❌ Pari introuvable" });
                return;
            }
            if (bet.Result != "PENDING")
            {
                await Send.OkAsync(new SetOddsResponse { Success = false, Message = "❌ Ce pari est déjà réglé" });
                return;
            }

            bet.Odds = req.Odds;
            await _context.SaveChangesAsync(ct);
            await Send.OkAsync(new SetOddsResponse { Success = true, Message = $"✅ Cote mise à jour: {req.Odds}" });
            return;
        }

        if (req.Kind == "leg")
        {
            var leg = await _context.ComboLegs
                .Include(l => l.BetCombo)
                .ThenInclude(c => c!.Legs)
                .FirstOrDefaultAsync(l => l.Id == req.Id, ct);
            if (leg == null)
            {
                await Send.OkAsync(new SetOddsResponse { Success = false, Message = "❌ Jambe de combiné introuvable" });
                return;
            }
            if (leg.Result != "PENDING")
            {
                await Send.OkAsync(new SetOddsResponse { Success = false, Message = "❌ Cette jambe est déjà réglée" });
                return;
            }
            if (leg.BetCombo == null)
            {
                await Send.OkAsync(new SetOddsResponse { Success = false, Message = "❌ Combiné parent introuvable" });
                return;
            }

            leg.Odds = req.Odds;

            // Recompute the combo's combined odds from every leg's current
            // value (real, manually entered, or still the flat estimate for
            // whichever legs haven't been corrected yet).
            if (leg.BetCombo.Result == "PENDING")
            {
                leg.BetCombo.CombinedOdds = leg.BetCombo.Legs.Aggregate(1m, (acc, l) => acc * l.Odds);
            }

            await _context.SaveChangesAsync(ct);
            await Send.OkAsync(new SetOddsResponse { Success = true, Message = $"✅ Cote de la jambe mise à jour: {req.Odds}" });
            return;
        }

        await Send.OkAsync(new SetOddsResponse { Success = false, Message = $"❌ Type inconnu: '{req.Kind}' (attendu 'bet' ou 'leg')" });
    }
}
