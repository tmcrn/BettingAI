namespace BettingAI.Models;

// A small, transparent logistic regression - a single row, updated in
// place by WinPredictionService after every bet/leg that actually gets
// settled (WIN or LOSS). This is the genuine machine-learning piece: real
// parameters that change from a real reward signal (1 for a win, 0 for a
// loss), via one step of online gradient descent per result - unlike the
// Learning Notebook (real stats, but only ever handed back as text for
// Mistral to interpret however it likes, with no guarantee it's used
// consistently). Deliberately tiny (4 features, one weight each, plus a
// bias) rather than a black box, and it starts at all-zero weights
// (sigmoid(0) = 50%, i.e. "no signal yet") rather than pretending to know
// anything before it's seen real results - see WinPredictionService for
// the feature definitions and the update rule.
public class LearnedModelWeights
{
    public int Id { get; set; }
    public decimal Bias { get; set; } = 0m;
    public decimal WeightEdgeAlignment { get; set; } = 0m;
    public decimal WeightFormAlignment { get; set; } = 0m;
    public decimal WeightMomentumAlignment { get; set; } = 0m;
    public decimal WeightConfidence { get; set; } = 0m;
    public int SampleCount { get; set; } = 0;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
