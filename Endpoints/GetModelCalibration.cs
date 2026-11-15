using BettingAI.Data;
using BettingAI.Services;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Endpoints;

public class CalibrationBucket
{
    public string Range { get; set; } = "";
    public int Count { get; set; }
    public decimal PredictedRate { get; set; }
    public decimal ActualRate { get; set; }
}

public class ModelCalibrationResponse
{
    // Value-engine bets only (placed with real odds + model probability).
    public int BetsPlaced { get; set; }
    public int BetsSettled { get; set; }
    public int Wins { get; set; }
    public decimal Staked { get; set; }
    public decimal Profit { get; set; }
    public decimal? Roi { get; set; }

    // Average expected value at decision time vs what really happened.
    public decimal? AverageExpectedValue { get; set; }

    // Closing line value: average of (odds taken / closing odds − 1) over
    // bets whose closing price was refreshed after placement. Positive =
    // we beat the market's final price = the clearest sign of a real edge.
    public int ClvSample { get; set; }
    public decimal? AverageClv { get; set; }
    public decimal? ClvPositiveShare { get; set; }

    // Brier score (lower = better; 0.25 = coin flip) of the probabilities
    // we bet with, and the same for the market's own probabilities on the
    // same bets - if ours isn't lower, the model adds nothing.
    public decimal? BrierModel { get; set; }
    public decimal? BrierMarket { get; set; }

    public List<CalibrationBucket> Buckets { get; set; } = new();
    public string? Verdict { get; set; }
    public string? OddsQuota { get; set; }
}

// "Is the model any good?" in numbers, the way pros judge a strategy:
// CLV first (meaningful after ~50 bets), calibration and ROI later
// (ROI needs hundreds of bets before it means anything).
public class GetModelCalibrationEndpoint : EndpointWithoutRequest<ModelCalibrationResponse>
{
    private readonly BettingContext _db;

    public GetModelCalibrationEndpoint(BettingContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/api/model-calibration");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var bets = await _db.Bets.Where(b => b.ModelProbability != null && b.Odds != null).ToListAsync(ct);
        var settled = bets.Where(b => b.Result == "WIN" || b.Result == "LOSS").ToList();

        var response = new ModelCalibrationResponse
        {
            BetsPlaced = bets.Count,
            BetsSettled = settled.Count,
            Wins = settled.Count(b => b.Result == "WIN"),
            Staked = settled.Sum(b => b.Stake),
            Profit = settled.Sum(b => (b.Winnings ?? 0) - b.Stake),
            AverageExpectedValue = bets.Count > 0 ? Math.Round(bets.Average(b => b.ExpectedValue ?? 0), 4) : null,
            OddsQuota = OddsApiService.LastQuotaInfo
        };
        response.Roi = response.Staked > 0 ? Math.Round(response.Profit / response.Staked, 4) : null;

        // CLV only counts when the closing price was refreshed AFTER placement.
        var clv = bets
            .Where(b => b.ClosingOdds is > 1 && b.ClosingOddsAt != null && b.ClosingOddsAt > b.CreatedAt.AddMinutes(5))
            .Select(b => b.Odds!.Value / b.ClosingOdds!.Value - 1)
            .ToList();
        response.ClvSample = clv.Count;
        if (clv.Count > 0)
        {
            response.AverageClv = Math.Round(clv.Average(), 4);
            response.ClvPositiveShare = Math.Round((decimal)clv.Count(v => v > 0) / clv.Count, 4);
        }

        if (settled.Count > 0)
        {
            decimal Outcome(Models.Bet b) => b.Result == "WIN" ? 1 : 0;
            response.BrierModel = Math.Round(settled.Average(b => (b.ModelProbability!.Value - Outcome(b)) * (b.ModelProbability!.Value - Outcome(b))), 4);
            var withMarket = settled.Where(b => b.MarketProbability != null).ToList();
            if (withMarket.Count > 0)
                response.BrierMarket = Math.Round(withMarket.Average(b => (b.MarketProbability!.Value - Outcome(b)) * (b.MarketProbability!.Value - Outcome(b))), 4);

            var edges = new[] { 0m, 0.3m, 0.45m, 0.6m, 1.01m };
            for (var i = 0; i < edges.Length - 1; i++)
            {
                var lo = edges[i];
                var hi = edges[i + 1];
                var inBucket = settled.Where(b => b.ModelProbability >= lo && b.ModelProbability < hi).ToList();
                if (inBucket.Count == 0) continue;
                response.Buckets.Add(new CalibrationBucket
                {
                    Range = $"{lo:P0}–{Math.Min(hi, 1m):P0}",
                    Count = inBucket.Count,
                    PredictedRate = Math.Round(inBucket.Average(b => b.ModelProbability!.Value), 4),
                    ActualRate = Math.Round((decimal)inBucket.Count(b => b.Result == "WIN") / inBucket.Count, 4)
                });
            }
        }

        response.Verdict = response.ClvSample < 30
            ? $"Trop tôt pour juger : {response.ClvSample} pari(s) avec une cote de clôture, il en faut ~50 pour que la CLV veuille dire quelque chose."
            : response.AverageClv > 0
                ? "CLV positive : on prend en moyenne de meilleures cotes que la cote finale du marché - signe d'un vrai avantage."
                : "CLV négative : le marché finit en moyenne plus haut que nous, le modèle n'a probablement pas d'avantage - à revoir avant d'augmenter les mises.";

        await Send.OkAsync(response);
    }
}
