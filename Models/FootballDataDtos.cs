namespace BettingAI.Models;

public class FootballDataResponse
{
    public List<Match>? Matches { get; set; }
}

public class Match
{
    public int Id { get; set; }
    public string? UtcDate { get; set; }
    public string? Status { get; set; }
    public Team? Home { get; set; }
    public Team? Away { get; set; }
    public Score? Score { get; set; }
}

public class Team
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

public class Score
{
    public Result? FullTime { get; set; }
}

public class Result
{
    public int? Home { get; set; }
    public int? Away { get; set; }
}
