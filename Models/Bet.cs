namespace BettingAI.Models;

public class Bet
{
    public int Id { get; set; }
    public string? MatchId { get; set; }
    public string? HomeTeam { get; set; }
    public string? AwayTeam { get; set; }

    // Display-only short names (e.g. "Union Berlin" instead of "1. FC Union
    // Berlin") - see the comment on FootballMatch.HomeTeamShort for why
    // HomeTeam/AwayTeam above stay the long official names regardless. Null
    // for bets placed before this existed, or when the API had none.
    public string? HomeTeamShort { get; set; }
    public string? AwayTeamShort { get; set; }

    // Club logo URL, same idea as HomeTeamShort - display only.
    public string? HomeTeamCrest { get; set; }
    public string? AwayTeamCrest { get; set; }
    public string? BetType { get; set; }  // PLAYER_SCORER, HOME_WIN, etc.
    public string? Selection { get; set; } // Nom joueur ou "2.5"
    public decimal Stake { get; set; }
    public decimal Confidence { get; set; }
    public string? Reasoning { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public decimal? Odds { get; set; }
    public string? Result { get; set; }  // "WIN", "LOSS", "PENDING"
    public decimal? Winnings { get; set; }
    public DateTime? MatchUtcDate { get; set; }  // Kickoff time - used to know when to check the real result

    // football-data.org's competition code (e.g. "FL1" for Ligue 1), copied
    // from FootballMatch at decision time - lets the dashboard show a flag
    // per league instead of just the team names.
    public string? CompetitionCode { get; set; }

    // The real final score, set once at settlement (auto or manual) -
    // previously the score entered/fetched was only used transiently to
    // compute WIN/LOSS then discarded, so a settled ticket showed the
    // outcome but never the actual score that produced it. Null while
    // PENDING.
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }

    // The learned model's input features AS THEY WERE at decision time
    // (see WinPredictionService.ComputeFeatures) - persisted rather than
    // recomputed at settlement time, since the underlying TeamStats will
    // have moved on by then (more matches played, form/momentum changed).
    // Training the model on stale, recomputed values instead of what the
    // decision actually saw would be learning from the wrong inputs. Null
    // for bets placed before this existed.
    public decimal? EdgeAlignmentFeature { get; set; }
    public decimal? FormAlignmentFeature { get; set; }
    public decimal? MomentumAlignmentFeature { get; set; }

    // ── Pro-style decision data (null for bets placed before the value-bet
    // engine existed) ──
    // Probability of THIS selection winning according to the Poisson model
    // alone, the market's fair probability (sharp bookmaker, margin
    // removed), and the blend of both actually used to decide.
    public decimal? PoissonProbability { get; set; }
    public decimal? MarketProbability { get; set; }
    public decimal? ModelProbability { get; set; }

    // Expected value at decision time: ModelProbability × Odds − 1.
    public decimal? ExpectedValue { get; set; }

    // Bookmaker whose price was taken (e.g. "winamax_fr") and The Odds API
    // event id, used to re-read the price right before kickoff.
    public string? Bookmaker { get; set; }
    public string? OddsEventId { get; set; }

    // Last price seen before kickoff (≈ closing line) and when. CLV =
    // Odds / ClosingOdds − 1: consistently beating the closing price is the
    // most reliable early sign of a real edge, long before win/loss
    // results are statistically meaningful.
    public decimal? ClosingOdds { get; set; }
    public DateTime? ClosingOddsAt { get; set; }
}
