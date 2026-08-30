namespace BettingAI.Models;

public class LearningNotebook
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdated { get; set; }

    // Patterns JSON: what works, what doesn't
    public string? Patterns { get; set; }

    // Failure analysis: why did we lose?
    public string? FailureAnalysis { get; set; }

    // Success patterns: conditions that lead to wins
    public string? SuccessPatterns { get; set; }

    // Current accuracy metrics
    public int TotalBets { get; set; }
    public int WonBets { get; set; }
    public decimal WinRate { get; set; }
    public decimal AverageConfidence { get; set; }
}
