using BettingAI.Models;

namespace BettingAI.Services;

// Pro-style goal model: each team's goals in a match follow a Poisson
// distribution whose mean (lambda) comes from its attack strength against
// the opponent's defence strength, relative to the average team. The
// Dixon-Coles correction then fixes plain Poisson's known weak spot (it
// under-predicts 0-0 / 1-1 draws and over-predicts 1-0 / 0-1). From the
// resulting score matrix every market we bet on (1X2, over/under X.5) is
// just a sum of cells - one consistent model for every bet type instead of
// an LLM eyeballing numbers.
//
// Inputs are TeamStats.xG / xGA, which on the free data source are really
// average goals scored / conceded (see TeamStatsSeedingService) - noisier
// than true xG, which is why strengths are shrunk toward average below and
// why DecideBets blends this model with the market rather than trusting it
// alone.
public sealed class PoissonModel
{
    // Home teams score ~11% above the average and away teams ~11% below in
    // the big European leagues (home goals ≈ 1.5, away ≈ 1.2 per match).
    private const double HomeFactor = 1.11;
    private const double AwayFactor = 0.89;

    // Regression to the mean: a team's raw ratio is only partly real skill,
    // partly a short-sample accident. 0.7 keeps 70% of the deviation.
    private const double Shrinkage = 0.7;

    // Dixon-Coles low-score dependence parameter - the published fits on
    // European leagues all land around -0.05 to -0.15.
    private const double Rho = -0.10;

    private const int MaxGoals = 10;

    private readonly double[,] _matrix = new double[MaxGoals + 1, MaxGoals + 1];

    public double LambdaHome { get; }
    public double LambdaAway { get; }
    public double HomeWin { get; }
    public double Draw { get; }
    public double AwayWin { get; }

    private PoissonModel(double lambdaHome, double lambdaAway)
    {
        LambdaHome = lambdaHome;
        LambdaAway = lambdaAway;

        double total = 0;
        for (var h = 0; h <= MaxGoals; h++)
        for (var a = 0; a <= MaxGoals; a++)
        {
            var p = Pmf(lambdaHome, h) * Pmf(lambdaAway, a) * Tau(h, a, lambdaHome, lambdaAway);
            _matrix[h, a] = Math.Max(0, p);
            total += _matrix[h, a];
        }

        double home = 0, draw = 0, away = 0;
        for (var h = 0; h <= MaxGoals; h++)
        for (var a = 0; a <= MaxGoals; a++)
        {
            _matrix[h, a] /= total;
            if (h > a) home += _matrix[h, a];
            else if (h == a) draw += _matrix[h, a];
            else away += _matrix[h, a];
        }
        HomeWin = home;
        Draw = draw;
        AwayWin = away;
    }

    public static PoissonModel FromLambdas(double lambdaHome, double lambdaAway) => new(lambdaHome, lambdaAway);

    // leagueAverageGoals = average goals scored per team per match across
    // every tracked team (the "1.0 strength" reference point).
    public static PoissonModel? FromTeamStats(TeamStats? home, TeamStats? away, double leagueAverageGoals)
    {
        if (home == null || away == null || leagueAverageGoals <= 0) return null;

        double Strength(decimal value) =>
            Math.Clamp(1 + Shrinkage * ((double)value / leagueAverageGoals - 1), 0.4, 2.5);

        var lambdaHome = leagueAverageGoals * HomeFactor * Strength(home.xG) * Strength(away.xGA);
        var lambdaAway = leagueAverageGoals * AwayFactor * Strength(away.xG) * Strength(home.xGA);
        return new PoissonModel(lambdaHome, lambdaAway);
    }

    // P(total goals > line), e.g. line 2.5 => 3+ goals.
    public double OverProbability(double line)
    {
        double p = 0;
        for (var h = 0; h <= MaxGoals; h++)
        for (var a = 0; a <= MaxGoals; a++)
            if (h + a > line) p += _matrix[h, a];
        return p;
    }

    // Most likely exact score - handy for the explanation text.
    public (int home, int away) MostLikelyScore()
    {
        var best = (0, 0);
        for (var h = 0; h <= MaxGoals; h++)
        for (var a = 0; a <= MaxGoals; a++)
            if (_matrix[h, a] > _matrix[best.Item1, best.Item2]) best = (h, a);
        return best;
    }

    private static double Pmf(double lambda, int k)
    {
        double result = Math.Exp(-lambda);
        for (var i = 1; i <= k; i++) result *= lambda / i;
        return result;
    }

    private static double Tau(int h, int a, double lh, double la) => (h, a) switch
    {
        (0, 0) => 1 - lh * la * Rho,
        (0, 1) => 1 + lh * Rho,
        (1, 0) => 1 + la * Rho,
        (1, 1) => 1 - Rho,
        _ => 1
    };
}
