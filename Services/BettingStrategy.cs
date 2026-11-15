using System.Globalization;

namespace BettingAI.Services;

// Every number that decides WHETHER and HOW MUCH to bet, in one place.
// Each can be overridden from .env without touching code; the defaults
// follow standard professional practice (value threshold, fractional
// Kelly, exposure caps) tuned conservative for a small bankroll and a
// still-unproven model.
public static class BettingStrategy
{
    // Minimum expected value to take a bet: p × odds − 1. 0.03 = we only bet
    // when we think we get at least 3% more than the fair price.
    public static decimal MinEdge => Env("BETTING_MIN_EDGE", 0.03m);

    // How much the final probability trusts our Poisson model vs the market
    // (sharp bookmaker, margin removed). 0.5 = halfway. Pros always shrink
    // toward the market: a model that disagrees wildly with Pinnacle is far
    // more often wrong than right.
    public static decimal ModelWeight => Env("BETTING_MODEL_WEIGHT", 0.5m);

    // Fraction of the full Kelly stake actually used. Full Kelly maximises
    // long-run growth only if the probabilities are exact - they never are,
    // so pros use 1/4 to 1/2. 0.25 = quarter Kelly.
    public static decimal KellyFraction => Env("BETTING_KELLY_FRACTION", 0.25m);

    // Never more than this share of the bankroll on one bet...
    public static decimal MaxStakeShare => Env("BETTING_MAX_STAKE_SHARE", 0.05m);

    // ...nor more than this share of the bankroll across one whole cycle.
    public static decimal MaxCycleExposureShare => Env("BETTING_MAX_CYCLE_SHARE", 0.25m);

    // Odds outside this range are skipped: very short prices carry tiny
    // edges eaten by any model error, long shots are where a crude model is
    // most overconfident.
    public static decimal MinOdds => Env("BETTING_MIN_ODDS", 1.40m);
    public static decimal MaxOdds => Env("BETTING_MAX_ODDS", 4.00m);

    // Minimum stake of every bet placed (smaller Kelly stakes are raised to
    // it - see KellyStake).
    public static decimal MinStake => Env("BETTING_MIN_STAKE", 0.10m);

    // Starting balance of the app's own simulated portfolio (dashboard
    // "solde", and what Kelly sizes from when BETTING_BANKROLL is unset).
    // Raise it (e.g. 100) for paper trading: stakes then follow the
    // simulated balance automatically as it grows or shrinks.
    public static decimal StartingBalance => Env("BETTING_START_BALANCE", 10m);

    // The real money you put aside for betting on the bookmaker. When set,
    // Kelly sizes from this; otherwise it falls back to the app's own
    // simulated balance (starts at 10€).
    public static decimal? Bankroll
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("BETTING_BANKROLL");
            return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
        }
    }

    // Kelly criterion: f* = (p·o − 1) / (o − 1), the bankroll share that
    // maximises long-run growth. Returns the actual stake after the Kelly
    // fraction and the per-bet cap, rounded down to 10 cents. MinStake is a
    // FLOOR: a value bet whose Kelly stake is smaller is raised to MinStake
    // rather than skipped (the owner's choice - e.g. 5€ minimum per bet),
    // and the per-bet cap never goes below that floor. 0 = no bet (no value,
    // or the balance can't cover the minimum).
    public static decimal KellyStake(decimal probability, decimal odds, decimal bankroll)
    {
        if (bankroll < MinStake || odds <= 1) return 0;
        var fullKelly = (probability * odds - 1) / (odds - 1);
        if (fullKelly <= 0) return 0;

        var cap = Math.Max(bankroll * MaxStakeShare, MinStake);
        var stake = Math.Clamp(bankroll * fullKelly * KellyFraction, MinStake, cap);
        return Math.Floor(stake * 10) / 10; // round DOWN to 0.10
    }

    // Removes the bookmaker margin: the implied probabilities 1/odds of all
    // outcomes of a market add up to more than 1 (that excess is the
    // margin); normalising them back to 1 gives the market's "fair" view.
    public static decimal[] RemoveVig(params decimal[] odds)
    {
        if (odds.Any(o => o <= 1)) return odds.Select(_ => 0m).ToArray();
        var implied = odds.Select(o => 1 / o).ToArray();
        var sum = implied.Sum();
        return implied.Select(p => p / sum).ToArray();
    }

    private static decimal Env(string name, decimal fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}
