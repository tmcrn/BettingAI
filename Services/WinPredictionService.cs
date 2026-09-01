using BettingAI.Data;
using BettingAI.Models;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Services;

// A small, transparent logistic regression - the genuine machine-learning
// piece of the system. Real parameters (LearnedModelWeights, a single row)
// that change from a real reward signal (1 for a win, 0 for a loss), via
// one step of online gradient descent per settled bet/leg. This is
// distinct from the Learning Notebook: that surfaces real stats as TEXT
// for Mistral to interpret however it likes, with no guarantee it's used
// consistently; this is literal weight updates from real outcomes, and its
// prediction is a genuine number computed the same way every time.
//
// Deliberately tiny (4 features, one weight each, plus a bias) rather than
// a black box that needs a huge sample to mean anything - which we don't
// have yet. It starts at all-zero weights (sigmoid(0) = 50%, "no signal
// yet") and is expected to be unreliable for a while; SampleCount is
// always surfaced alongside any prediction so nothing pretends to more
// certainty than it has earned.
public class WinPredictionService
{
    // Deliberately not tuned/validated - a reasonable starting point for
    // online SGD on a handful of examples per day, not a claim of
    // optimality. L2 keeps weights from swinging wildly on a tiny sample.
    private const decimal LearningRate = 0.1m;
    private const decimal L2 = 0.001m;

    private readonly BettingContext _context;

    public WinPredictionService(BettingContext context)
    {
        _context = context;
    }

    public record Features(decimal EdgeAlignment, decimal FormAlignment, decimal MomentumAlignment, decimal AiConfidence);

    public record Prediction(decimal Probability, int SampleCount);

    // Signed alignment: positive means the raw edge/form/momentum value
    // SUPPORTS this bet type's own favored side (e.g. a HOME_WIN bet gets
    // +xgEdgeValue when xgEdgeValue > 0 means home is favored; an AWAY_WIN
    // bet gets the sign flipped, so the same underlying number always means
    // "supports this bet" when positive). Bet types with no inherent side
    // (DRAW, OVER_GOALS, UNDER_GOALS, BOTH_TEAMS_SCORE) get 0 for these -
    // a real limitation of this first version: the model leans on
    // AiConfidence alone for those types today, rather than a fabricated
    // direction that wouldn't mean anything for a non-directional bet.
    public static Features ComputeFeatures(string? betType, decimal aiConfidence, decimal xgEdgeValue, decimal formEdgeValue, decimal momentumEdgeValue)
    {
        var sign = betType switch
        {
            "HOME_WIN" or "HOME_WIN_OR_DRAW" or "HOME_OVER_GOALS" => 1m,
            "AWAY_WIN" or "AWAY_WIN_OR_DRAW" or "AWAY_OVER_GOALS" => -1m,
            _ => 0m
        };

        return new Features(sign * xgEdgeValue, sign * formEdgeValue, sign * momentumEdgeValue, aiConfidence);
    }

    public async Task<Prediction> PredictAsync(Features f, CancellationToken ct = default)
    {
        var w = await GetOrCreateWeightsAsync(ct);
        var probability = Sigmoid(LinearCombination(w, f));
        return new Prediction(probability, w.SampleCount);
    }

    // One step of online (stochastic) gradient descent on the logistic
    // loss for a single real result - this is the actual "learning" step,
    // called once per bet/leg as soon as it's settled (see
    // BetSettlementService). Nothing here is retroactive or batch; each
    // result nudges the weights a little and is then forgotten, same as
    // classic online logistic regression.
    public async Task UpdateAsync(Features f, bool won, CancellationToken ct = default)
    {
        var w = await GetOrCreateWeightsAsync(ct);
        var predicted = Sigmoid(LinearCombination(w, f));
        var y = won ? 1m : 0m;
        var error = predicted - y; // d(loss)/d(z) for logistic loss

        w.Bias -= LearningRate * error;
        w.WeightEdgeAlignment -= LearningRate * (error * f.EdgeAlignment + L2 * w.WeightEdgeAlignment);
        w.WeightFormAlignment -= LearningRate * (error * f.FormAlignment + L2 * w.WeightFormAlignment);
        w.WeightMomentumAlignment -= LearningRate * (error * f.MomentumAlignment + L2 * w.WeightMomentumAlignment);
        w.WeightConfidence -= LearningRate * (error * f.AiConfidence + L2 * w.WeightConfidence);
        w.SampleCount += 1;
        w.LastUpdated = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
    }

    private static decimal LinearCombination(LearnedModelWeights w, Features f) =>
        w.Bias
        + w.WeightEdgeAlignment * f.EdgeAlignment
        + w.WeightFormAlignment * f.FormAlignment
        + w.WeightMomentumAlignment * f.MomentumAlignment
        + w.WeightConfidence * f.AiConfidence;

    private async Task<LearnedModelWeights> GetOrCreateWeightsAsync(CancellationToken ct)
    {
        var weights = await _context.LearnedModelWeights.FirstOrDefaultAsync(ct);
        if (weights == null)
        {
            weights = new LearnedModelWeights();
            _context.LearnedModelWeights.Add(weights);
            await _context.SaveChangesAsync(ct);
        }
        return weights;
    }

    // decimal has no built-in exp() - cast to double for the sigmoid, then
    // back. Precision loss here is irrelevant at these magnitudes.
    private static decimal Sigmoid(decimal z)
    {
        var d = (double)z;
        return (decimal)(1.0 / (1.0 + Math.Exp(-d)));
    }
}
