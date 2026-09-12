using BettingAI.Data;
using BettingAI.Services;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Endpoints;

public class SetOddsRequest
{
    public string Kind { get; set; } = ""; // "bet" | "leg" | "combo"
    public int Id { get; set; }
    public decimal Odds { get; set; }
}

public class SetOddsResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

// Lets the user directly enter the real odds they see on their own
// bookmaker for a bet/combo leg (there is no automatic scrape anymore -
// odds are always hand-entered), instead of relying on the confidence-
// derived estimate. Only while PENDING - once a bet is settled its payout
// is already computed and fixed.
public class SetOddsEndpoint : Endpoint<SetOddsRequest, SetOddsResponse>
{
    private readonly BettingContext _context;
    private readonly OddsLearningService _oddsLearning;

    public SetOddsEndpoint(BettingContext context, OddsLearningService oddsLearning)
    {
        _context = context;
        _oddsLearning = oddsLearning;
    }

    public override void Configure()
    {
        Post("/api/set-odds");
        AllowAnonymous();
    }

    public override async Task HandleAsync(SetOddsRequest req, CancellationToken ct)
    {
        if (!OwnerAuth.IsAuthorized(HttpContext))
        {
            HttpContext.Response.StatusCode = 403;
            return;
        }

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
            // Real odds the user typed in - a genuine training example for
            // OddsLearningService, same idea as WinPredictionService but for
            // "what does this bet type's real market odds usually look like".
            await _oddsLearning.RecordRealOddsAsync(bet.BetType, req.Odds, ct);
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
            // whichever legs haven't been corrected yet) - see
            // ComboOddsCalculator for why this isn't a plain product once
            // two legs share the same match. Skipped once the user has
            // typed in the combo's own real "cote totale" directly (see the
            // "combo" kind below) - that real number is trusted over the
            // formula from then on, so a later leg edit must not clobber it.
            if (leg.BetCombo.Result == "PENDING" && !leg.BetCombo.CombinedOddsIsManual)
            {
                leg.BetCombo.CombinedOdds = ComboOddsCalculator.Calculate(
                    leg.BetCombo.Legs.Select(l => (l.MatchId, l.BetType, l.Odds)));
            }

            await _context.SaveChangesAsync(ct);
            await _oddsLearning.RecordRealOddsAsync(leg.BetType, req.Odds, ct);
            await Send.OkAsync(new SetOddsResponse { Success = true, Message = $"✅ Cote de la jambe mise à jour: {req.Odds}" });
            return;
        }

        if (req.Kind == "combo")
        {
            // Bypasses ComboOddsCalculator's formula entirely - for when the
            // user already has the exact "cote totale" as displayed on
            // their own bookmaker (e.g. Winamax) and would rather trust
            // that real number than any same-match correlation estimate,
            // however well calibrated. Marks CombinedOddsIsManual so a
            // later per-leg odds edit (see the "leg" branch above) doesn't
            // silently overwrite it again.
            var combo = await _context.BetCombos.FirstOrDefaultAsync(c => c.Id == req.Id, ct);
            if (combo == null)
            {
                await Send.OkAsync(new SetOddsResponse { Success = false, Message = "❌ Combiné introuvable" });
                return;
            }
            if (combo.Result != "PENDING")
            {
                await Send.OkAsync(new SetOddsResponse { Success = false, Message = "❌ Ce combiné est déjà réglé" });
                return;
            }

            combo.CombinedOdds = req.Odds;
            combo.CombinedOddsIsManual = true;
            await _context.SaveChangesAsync(ct);
            await Send.OkAsync(new SetOddsResponse { Success = true, Message = $"✅ Cote totale mise à jour: {req.Odds}" });
            return;
        }

        await Send.OkAsync(new SetOddsResponse { Success = false, Message = $"❌ Type inconnu: '{req.Kind}' (attendu 'bet', 'leg' ou 'combo')" });
    }
}
