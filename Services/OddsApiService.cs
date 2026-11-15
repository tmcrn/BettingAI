using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BettingAI.Models;

namespace BettingAI.Services;

// Real bookmaker odds from The Odds API (the-odds-api.com, free plan = 500
// credits/month). One call per league returns every upcoming match of that
// league with 1X2 ("h2h") and over/under ("totals") prices from the
// bookmakers we ask for:
//   - the one we actually bet on (Winamax by default) - the price we'd get,
//   - a "sharp" one (Pinnacle by default) - low margin, accepts winners,
//     the market's best estimate of the true probability.
// Cost: markets × regions per call. Asking for ≤10 bookmakers counts as one
// region, so h2h+totals = 2 credits per league per call.
public class OddsApiService
{
    private static string ApiKey => Environment.GetEnvironmentVariable("THE_ODDS_API_KEY") ?? "";
    public static string BetBookmaker => Environment.GetEnvironmentVariable("ODDS_BOOKMAKER") ?? "winamax_fr";
    public static string SharpBookmaker => Environment.GetEnvironmentVariable("ODDS_SHARP_BOOKMAKER") ?? "pinnacle";
    private static string BaseUrl => Environment.GetEnvironmentVariable("ODDS_API_BASE_URL") ?? "https://api.the-odds-api.com/v4";

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    // football-data.org competition code -> The Odds API sport key.
    public static readonly Dictionary<string, string> SportKeys = new()
    {
        ["PL"] = "soccer_epl",
        ["PD"] = "soccer_spain_la_liga",
        ["SA"] = "soccer_italy_serie_a",
        ["BL1"] = "soccer_germany_bundesliga",
        ["FL1"] = "soccer_france_ligue_one",
        ["CL"] = "soccer_uefa_champs_league",
        ["PPL"] = "soccer_portugal_primeira_liga",
    };

    private readonly HttpClient _http;

    public OddsApiService(HttpClient http)
    {
        _http = http;
    }

    // Last quota numbers seen in a response header, for the logs/dashboard.
    public static string? LastQuotaInfo { get; private set; }

    public async Task<List<OddsEvent>> GetLeagueOddsAsync(string competitionCode, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("THE_ODDS_API_KEY non configurée dans .env");
        if (!SportKeys.TryGetValue(competitionCode, out var sportKey)) return new();

        var bookmakers = string.Join(",", new[] { BetBookmaker, SharpBookmaker }.Distinct());
        var url = $"{BaseUrl}/sports/{sportKey}/odds?apiKey={Uri.EscapeDataString(ApiKey)}" +
                  $"&bookmakers={bookmakers}&markets=h2h,totals&oddsFormat=decimal&dateFormat=iso";

        using var response = await _http.GetAsync(url, ct);
        if (response.Headers.TryGetValues("x-requests-remaining", out var remaining))
        {
            var used = response.Headers.TryGetValues("x-requests-used", out var u) ? u.FirstOrDefault() : "?";
            LastQuotaInfo = $"{remaining.FirstOrDefault()} crédits restants ({used} utilisés ce mois)";
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The Odds API {(int)response.StatusCode} pour {competitionCode} ({sportKey}): {Truncate(body, 300)}");
        }

        var events = JsonSerializer.Deserialize<List<OddsEvent>>(body, JsonOptions) ?? new();
        Console.WriteLine($"💶 Cotes {competitionCode}: {events.Count} matchs. {LastQuotaInfo}");
        return events;
    }

    // Pairs a football-data.org match with the same match in The Odds API
    // feed. The two sources spell clubs differently ("Paris Saint-Germain
    // FC" vs "Paris Saint Germain", "FC Internazionale Milano" vs "Inter
    // Milan"), so this compares normalised name tokens, restricted to
    // events kicking off within 2h of the real kickoff, and keeps the pair
    // whose two team names agree best.
    public static OddsEvent? FindEvent(FootballMatch match, IEnumerable<OddsEvent> events)
    {
        OddsEvent? best = null;
        double bestScore = 0;
        foreach (var ev in events)
        {
            if (Math.Abs((ev.CommenceTime - match.UtcDate).TotalHours) > 2) continue;
            var home = Math.Max(NameSimilarity(match.HomeTeam, ev.HomeTeam), NameSimilarity(match.HomeTeamShort, ev.HomeTeam));
            var away = Math.Max(NameSimilarity(match.AwayTeam, ev.AwayTeam), NameSimilarity(match.AwayTeamShort, ev.AwayTeam));
            if (home < 0.5 || away < 0.5) continue;
            if (home + away > bestScore)
            {
                bestScore = home + away;
                best = ev;
            }
        }
        return best;
    }

    // Overlap coefficient of normalised tokens: |A∩B| / min(|A|,|B|), so
    // "Marseille" fully matches "Olympique de Marseille".
    public static double NameSimilarity(string? a, string? b)
    {
        var ta = Tokens(a);
        var tb = Tokens(b);
        if (ta.Count == 0 || tb.Count == 0) return 0;
        return (double)ta.Intersect(tb).Count() / Math.Min(ta.Count, tb.Count);
    }

    private static readonly HashSet<string> StopTokens = new()
    {
        "fc", "cf", "afc", "ac", "as", "sc", "ssc", "sv", "vfb", "vfl", "tsg", "rc", "rcd", "ca", "cd", "ud", "sd",
        "us", "ogc", "losc", "club", "de", "la", "le", "les", "del", "da", "do", "of", "and", "the", "calcio",
        "futebol", "futbol", "football", "bc", "fk", "sk", "1", "04", "05", "1846", "1899", "1900", "1901", "1907",
        "1909", "1910", "1913", "balompie", "sad", "cp", "hsc", "clube", "e"
    };

    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["internazionale"] = "inter",
        ["milano"] = "milan",
        ["munchen"] = "munich",
        ["koln"] = "cologne",
        ["psg"] = "paris",
        ["wolves"] = "wolverhampton",
        ["spurs"] = "tottenham",
        ["utd"] = "united",
        ["man"] = "manchester",
        ["nottm"] = "nottingham",
        ["saint"] = "st",
        ["sankt"] = "st",
        ["gladbach"] = "monchengladbach",
        ["lisboa"] = "lisbon",
        ["rennais"] = "rennes",
        ["brestois"] = "brest",
        ["lyonnais"] = "lyon",
        ["havrais"] = "havre",
    };

    private static HashSet<string> Tokens(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return new();
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        var tokens = sb.ToString().Replace("ß", "ss")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => Aliases.TryGetValue(t, out var alias) ? alias : t)
            .Where(t => !StopTokens.Contains(t))
            .ToHashSet();
        return tokens;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}

public class OddsEvent
{
    public string Id { get; set; } = "";
    [JsonPropertyName("sport_key")] public string? SportKey { get; set; }
    [JsonPropertyName("commence_time")] public DateTime CommenceTime { get; set; }
    [JsonPropertyName("home_team")] public string? HomeTeam { get; set; }
    [JsonPropertyName("away_team")] public string? AwayTeam { get; set; }
    public List<OddsBookmaker> Bookmakers { get; set; } = new();

    public OddsBookmaker? Bookmaker(string key) => Bookmakers.FirstOrDefault(b => b.Key == key);
}

public class OddsBookmaker
{
    public string Key { get; set; } = "";
    public string? Title { get; set; }
    public List<OddsMarket> Markets { get; set; } = new();

    // (home, draw, away) decimal prices, or null if this bookmaker has no 1X2.
    public (decimal home, decimal draw, decimal away)? H2H(string? homeTeam, string? awayTeam)
    {
        var market = Markets.FirstOrDefault(m => m.Key == "h2h");
        if (market == null) return null;
        var home = market.Outcomes.FirstOrDefault(o => o.Name == homeTeam)?.Price;
        var away = market.Outcomes.FirstOrDefault(o => o.Name == awayTeam)?.Price;
        var draw = market.Outcomes.FirstOrDefault(o => o.Name == "Draw")?.Price;
        return home is > 1 && away is > 1 && draw is > 1 ? (home.Value, draw.Value, away.Value) : null;
    }

    // Every over/under line this bookmaker offers: point -> (over, under).
    public Dictionary<decimal, (decimal over, decimal under)> Totals()
    {
        var result = new Dictionary<decimal, (decimal over, decimal under)>();
        var market = Markets.FirstOrDefault(m => m.Key == "totals");
        if (market == null) return result;
        foreach (var group in market.Outcomes.Where(o => o.Point != null).GroupBy(o => o.Point!.Value))
        {
            var over = group.FirstOrDefault(o => o.Name == "Over")?.Price;
            var under = group.FirstOrDefault(o => o.Name == "Under")?.Price;
            if (over is > 1 && under is > 1) result[group.Key] = (over.Value, under.Value);
        }
        return result;
    }
}

public class OddsMarket
{
    public string Key { get; set; } = "";
    public List<OddsOutcome> Outcomes { get; set; } = new();
}

public class OddsOutcome
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public decimal? Point { get; set; }
}
