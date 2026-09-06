namespace BettingAI.Models;

public class ComboLeg
{
    public int Id { get; set; }
    public int BetComboId { get; set; }
    public BetCombo? BetCombo { get; set; }

    public string? MatchId { get; set; }
    public string? HomeTeam { get; set; }
    public string? AwayTeam { get; set; }
    public string? HomeTeamShort { get; set; } // see the comment on Bet.HomeTeamShort
    public string? AwayTeamShort { get; set; }
    public string? HomeTeamCrest { get; set; } // see the comment on Bet.HomeTeamCrest
    public string? AwayTeamCrest { get; set; }
    public string? BetType { get; set; } // any bet type, incl. non-1X2 (goal lines etc.)
    public string? Selection { get; set; } // the line for goal-based types, e.g. "2.5" - null for 1X2-family types
    public decimal Odds { get; set; } // resolved real odds for this leg's selection
    public DateTime? MatchUtcDate { get; set; }
    public string? CompetitionCode { get; set; } // see the comment on Bet.CompetitionCode
    public string Result { get; set; } = "PENDING"; // PENDING, WIN, LOSS

    // The real final score for this leg's own match, set once at
    // settlement (auto or manual) - see the comment on Bet.HomeScore for
    // why this wasn't already tracked. Null while PENDING.
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }

    // The combo's own Confidence is the PRODUCT across all legs, not any
    // one leg's individual confidence - not usable as a training feature
    // for this specific leg's own win/loss. This is the leg's own value:
    // for a merged OUTCOME+GOALS combo (see MergeOutcomeAndGoalsIntoCombo),
    // it's that leg's original confidence before merging; for a combo the
    // AI proposed natively, it falls back to the whole bet's confidence
    // (the only value available per leg in that case).
    public decimal? Confidence { get; set; }

    // Same as Bet's - the learned model's input features as they were at
    // decision time, persisted for training later rather than recomputed.
    public decimal? EdgeAlignmentFeature { get; set; }
    public decimal? FormAlignmentFeature { get; set; }
    public decimal? MomentumAlignmentFeature { get; set; }
}
