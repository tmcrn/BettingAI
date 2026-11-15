using System.Globalization;

namespace BettingAI.Services;

// One possible bet on one match, fully priced: what our model thinks, what
// the market thinks, the price we'd get and the resulting expected value.
public record ValueCandidate(
    string BetType,          // HOME_WIN / DRAW / AWAY_WIN / OVER_GOALS / UNDER_GOALS
    string? Selection,       // goal line for totals ("2.5"), null for 1X2
    decimal Odds,            // price at the bookmaker we bet on
    decimal PoissonProbability,
    decimal MarketProbability,
    decimal FinalProbability,
    decimal ExpectedValue,
    bool MarketFromSharp)    // fair probability came from the sharp book (vs our own book's prices)
{
    public string Label => BetType switch
    {
        "HOME_WIN" => "Victoire domicile",
        "DRAW" => "Match nul",
        "AWAY_WIN" => "Victoire extérieur",
        "OVER_GOALS" => $"Plus de {Selection} buts",
        "UNDER_GOALS" => $"Moins de {Selection} buts",
        _ => BetType
    };
}

// Turns (Poisson model + one match's odds) into every priced bet the
// bookmaker offers, and picks out the ones with real value.
public static class ValueBetFinder
{
    private static readonly decimal[] GoalLines = { 1.5m, 2.5m, 3.5m };

    public static List<ValueCandidate> AllCandidates(PoissonModel model, OddsEvent ev, string betBookmaker, string sharpBookmaker)
    {
        var result = new List<ValueCandidate>();
        var book = ev.Bookmaker(betBookmaker);
        if (book == null) return result; // can't bet a match our bookmaker doesn't price
        var sharp = ev.Bookmaker(sharpBookmaker);
        var weight = Math.Clamp(BettingStrategy.ModelWeight, 0m, 1m);

        ValueCandidate Make(string type, string? selection, decimal odds, double modelP, decimal marketP, bool fromSharp)
        {
            var poisson = (decimal)modelP;
            var final = weight * poisson + (1 - weight) * marketP;
            return new ValueCandidate(type, selection, odds, poisson, marketP, final, final * odds - 1, fromSharp);
        }

        // 1X2
        var bookH2H = book.H2H(ev.HomeTeam, ev.AwayTeam);
        if (bookH2H != null)
        {
            var sharpH2H = sharp?.H2H(ev.HomeTeam, ev.AwayTeam);
            var fairSource = sharpH2H ?? bookH2H.Value;
            var fair = BettingStrategy.RemoveVig(fairSource.home, fairSource.draw, fairSource.away);
            var fromSharp = sharpH2H != null;
            result.Add(Make("HOME_WIN", null, bookH2H.Value.home, model.HomeWin, fair[0], fromSharp));
            result.Add(Make("DRAW", null, bookH2H.Value.draw, model.Draw, fair[1], fromSharp));
            result.Add(Make("AWAY_WIN", null, bookH2H.Value.away, model.AwayWin, fair[2], fromSharp));
        }

        // Over / under
        var bookTotals = book.Totals();
        var sharpTotals = sharp?.Totals() ?? new();
        foreach (var line in GoalLines)
        {
            if (!bookTotals.TryGetValue(line, out var prices)) continue;
            var fromSharp = sharpTotals.TryGetValue(line, out var sharpPrices);
            var fairSource = fromSharp ? sharpPrices : prices;
            var fair = BettingStrategy.RemoveVig(fairSource.over, fairSource.under);
            var overP = model.OverProbability((double)line);
            var selection = line.ToString("0.0", CultureInfo.InvariantCulture);
            result.Add(Make("OVER_GOALS", selection, prices.over, overP, fair[0], fromSharp));
            result.Add(Make("UNDER_GOALS", selection, prices.under, 1 - overP, fair[1], fromSharp));
        }

        return result;
    }

    public static bool IsValue(ValueCandidate c) =>
        c.ExpectedValue >= BettingStrategy.MinEdge &&
        c.Odds >= BettingStrategy.MinOdds &&
        c.Odds <= BettingStrategy.MaxOdds;

    // Current price of an already-placed bet in a fresh odds feed - used to
    // record the closing line (CLV).
    public static decimal? PriceFor(OddsEvent ev, string bookmaker, string? betType, string? selection)
    {
        var book = ev.Bookmaker(bookmaker);
        if (book == null) return null;
        switch (betType)
        {
            case "HOME_WIN":
            case "DRAW":
            case "AWAY_WIN":
                var h2h = book.H2H(ev.HomeTeam, ev.AwayTeam);
                if (h2h == null) return null;
                return betType == "HOME_WIN" ? h2h.Value.home : betType == "DRAW" ? h2h.Value.draw : h2h.Value.away;
            case "OVER_GOALS":
            case "UNDER_GOALS":
                if (!decimal.TryParse(selection, NumberStyles.Any, CultureInfo.InvariantCulture, out var line)) return null;
                if (!book.Totals().TryGetValue(line, out var t)) return null;
                return betType == "OVER_GOALS" ? t.over : t.under;
            default:
                return null;
        }
    }
}
