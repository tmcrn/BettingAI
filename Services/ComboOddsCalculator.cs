namespace BettingAI.Services;

// Combines each leg's own odds into one combo price, correcting for the
// real-world correlation between two selections on the SAME match - a
// plain product of each leg's own odds is right for legs on different,
// independent matches, but overstates the true combined odds once both
// legs are on the same match and the outcomes aren't independent (e.g. the
// home team winning makes "plus de 2.5 buts" - and even more so "le
// domicile marque 2+ buts lui-même" - both more likely than the raw
// product assumes). Winamax's own displayed "cote totale" for these
// same-match combos is consistently lower than leg1.Odds * leg2.Odds.
//
// SameMatchCorrelationFactor is applied as (product of that match's leg
// odds) / factor - factor > 1 means "this pair's naive product overstates
// the real combined odds by this much". Calibrated from real Winamax combo
// slips (Sept 2026), comparing this app's naive product against Winamax's
// own displayed total for the exact same two legs:
//   HOME_WIN + OVER_GOALS:      naive 2.29 -> real 1.96 (factor 1.168)
//                               naive 1.54 -> real 1.43 (factor 1.077)
//                               -> averaged to 1.12
//   AWAY_WIN + AWAY_OVER_GOALS: naive 7.17 -> real 3.35 (factor 2.140)
// Only pairs actually calibrated against a real example are in the table;
// anything else (including 3+ legs on one match, which we have no real
// data for) keeps the plain product rather than guessing at an
// uncalibrated number. Add more entries here as more real examples come
// in - the more lopsided the naive/real ratio, the more redundant the two
// bet types are with each other.
public static class ComboOddsCalculator
{
    private static readonly Dictionary<(string, string), decimal> SameMatchCorrelationFactor = new()
    {
        // Match winner + total match goal line - moderately correlated (a
        // team winning tends to come with a somewhat higher-scoring game,
        // but far from guaranteed either way).
        [OrderedKey("HOME_WIN", "OVER_GOALS")] = 1.12m,
        [OrderedKey("AWAY_WIN", "OVER_GOALS")] = 1.12m,

        // A team winning + that SAME team scoring over its own goal line -
        // much more strongly correlated (winning while barely scoring
        // yourself is rare once the line is 1.5+), hence the much bigger
        // discount than the total-match-goals pair above.
        [OrderedKey("HOME_WIN", "HOME_OVER_GOALS")] = 2.14m,
        [OrderedKey("AWAY_WIN", "AWAY_OVER_GOALS")] = 2.14m,
    };

    // Unordered pair key so ("HOME_WIN","OVER_GOALS") and
    // ("OVER_GOALS","HOME_WIN") hit the same table entry.
    private static (string, string) OrderedKey(string a, string b)
        => string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);

    // legs: every leg's (MatchId, BetType, Odds). Legs are grouped by
    // MatchId - legs on different matches are always independent and just
    // multiply together; only a group of exactly 2 legs sharing the same
    // MatchId gets looked up in SameMatchCorrelationFactor (the only shape
    // this app actually produces today - see MergeOutcomeAndGoalsIntoCombo
    // and TryBuildCombo's "one 1X2-family leg + one goal-line leg per
    // match" rule). A lone leg on a match needs no correction; 3+ legs on
    // one match has no calibrated data so it's left as a plain product too.
    public static decimal Calculate(IEnumerable<(string? MatchId, string? BetType, decimal Odds)> legs)
    {
        var combined = 1m;

        foreach (var group in legs.GroupBy(l => l.MatchId))
        {
            var groupList = group.ToList();
            var groupProduct = groupList.Aggregate(1m, (acc, l) => acc * l.Odds);

            if (groupList.Count == 2 && group.Key != null)
            {
                var typeA = groupList[0].BetType ?? "";
                var typeB = groupList[1].BetType ?? "";
                if (SameMatchCorrelationFactor.TryGetValue(OrderedKey(typeA, typeB), out var factor))
                {
                    groupProduct /= factor;
                }
            }

            combined *= groupProduct;
        }

        return combined;
    }
}
