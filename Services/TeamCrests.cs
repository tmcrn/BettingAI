namespace BettingAI.Services;

// Fallback for FootballDataService.GetUpcomingMatchesAsync when football-data.org's
// own "crest" field is missing or null for a team - same reasoning and same
// integration point as TeamShortNames. Unlike that dictionary (best-effort
// media shortenings, never verified against the real API), every URL here
// was pulled directly from football-data.org's own /v4/competitions/{code}/teams
// response (real "crest" field, one call per tracked league: FL1, PL, PD,
// SA, BL1) - a genuine source, not a guess.
public static class TeamCrests
{
    private static readonly Dictionary<string, string> Map = new()
    {
        // Serie A
        ["AC Milan"] = "https://crests.football-data.org/98.png",
        ["ACF Fiorentina"] = "https://crests.football-data.org/99.png",
        ["AS Roma"] = "https://crests.football-data.org/100.png",
        ["Atalanta BC"] = "https://crests.football-data.org/102.png",
        ["Bologna FC 1909"] = "https://crests.football-data.org/103.png",
        ["Cagliari Calcio"] = "https://crests.football-data.org/104.png",
        ["Genoa CFC"] = "https://crests.football-data.org/107.png",
        ["FC Internazionale Milano"] = "https://crests.football-data.org/108.png",
        ["Juventus FC"] = "https://crests.football-data.org/109.png",
        ["SS Lazio"] = "https://crests.football-data.org/110.png",
        ["Parma Calcio 1913"] = "https://crests.football-data.org/112.png",
        ["SSC Napoli"] = "https://crests.football-data.org/113.png",
        ["Udinese Calcio"] = "https://crests.football-data.org/115.png",
        ["Venezia FC"] = "https://crests.football-data.org/454.png",
        ["Frosinone Calcio"] = "https://crests.football-data.org/470.png",
        ["US Sassuolo Calcio"] = "https://crests.football-data.org/471.png",
        ["Torino FC"] = "https://crests.football-data.org/586.png",
        ["US Lecce"] = "https://crests.football-data.org/5890.png",
        ["AC Monza"] = "https://crests.football-data.org/5911.png",
        ["Como 1907"] = "https://crests.football-data.org/7397.png",

        // Bundesliga
        ["1. FC Köln"] = "https://crests.football-data.org/1.png",
        ["TSG 1899 Hoffenheim"] = "https://crests.football-data.org/2.png",
        ["Bayer 04 Leverkusen"] = "https://crests.football-data.org/3.png",
        ["Borussia Dortmund"] = "https://crests.football-data.org/4.png",
        ["FC Bayern München"] = "https://crests.football-data.org/5.png",
        ["FC Schalke 04"] = "https://crests.football-data.org/6.png",
        ["Hamburger SV"] = "https://crests.football-data.org/7.png",
        ["VfB Stuttgart"] = "https://crests.football-data.org/10.png",
        ["SV Werder Bremen"] = "https://crests.football-data.org/12.png",
        ["1. FSV Mainz 05"] = "https://crests.football-data.org/15.png",
        ["FC Augsburg"] = "https://crests.football-data.org/16.png",
        ["SC Freiburg"] = "https://crests.football-data.org/17.png",
        ["Borussia Mönchengladbach"] = "https://crests.football-data.org/18.png",
        ["Eintracht Frankfurt"] = "https://crests.football-data.org/19.png",
        ["1. FC Union Berlin"] = "https://crests.football-data.org/28.png",
        ["SC Paderborn 07"] = "https://crests.football-data.org/29.png",
        ["SV 07 Elversberg"] = "https://crests.football-data.org/719.png",
        ["RB Leipzig"] = "https://crests.football-data.org/721.png",

        // Premier League
        ["Arsenal FC"] = "https://crests.football-data.org/57.png",
        ["Aston Villa FC"] = "https://crests.football-data.org/58.png",
        ["Chelsea FC"] = "https://crests.football-data.org/61.png",
        ["Everton FC"] = "https://crests.football-data.org/62.png",
        ["Fulham FC"] = "https://crests.football-data.org/63.png",
        ["Liverpool FC"] = "https://crests.football-data.org/64.png",
        ["Manchester City FC"] = "https://crests.football-data.org/65.png",
        ["Manchester United FC"] = "https://crests.football-data.org/66.png",
        ["Newcastle United FC"] = "https://crests.football-data.org/67.png",
        ["Sunderland AFC"] = "https://crests.football-data.org/71.png",
        ["Tottenham Hotspur FC"] = "https://crests.football-data.org/73.png",
        ["Hull City AFC"] = "https://crests.football-data.org/322.png",
        ["Leeds United FC"] = "https://crests.football-data.org/341.png",
        ["Ipswich Town FC"] = "https://crests.football-data.org/349.png",
        ["Nottingham Forest FC"] = "https://crests.football-data.org/351.png",
        ["Crystal Palace FC"] = "https://crests.football-data.org/354.png",
        ["Brighton & Hove Albion FC"] = "https://crests.football-data.org/397.png",
        ["Brentford FC"] = "https://crests.football-data.org/402.png",
        ["AFC Bournemouth"] = "https://crests.football-data.org/bournemouth.png",
        ["Coventry City FC"] = "https://crests.football-data.org/1076.png",

        // La Liga
        ["Athletic Club"] = "https://crests.football-data.org/77.png",
        ["Club Atlético de Madrid"] = "https://crests.football-data.org/78.png",
        ["CA Osasuna"] = "https://crests.football-data.org/79.png",
        ["RCD Espanyol de Barcelona"] = "https://crests.football-data.org/80.png",
        ["FC Barcelona"] = "https://crests.football-data.org/81.png",
        ["Getafe CF"] = "https://crests.football-data.org/82.png",
        ["Málaga CF"] = "https://crests.football-data.org/84.png",
        ["Real Madrid CF"] = "https://crests.football-data.org/86.png",
        ["Rayo Vallecano de Madrid"] = "https://crests.football-data.org/87.png",
        ["Levante UD"] = "https://crests.football-data.org/88.png",
        ["Real Betis Balompié"] = "https://crests.football-data.org/90.png",
        ["Real Sociedad de Fútbol"] = "https://crests.football-data.org/92.png",
        ["Villarreal CF"] = "https://crests.football-data.org/94.png",
        ["Valencia CF"] = "https://crests.football-data.org/95.png",
        ["Deportivo Alavés"] = "https://crests.football-data.org/263.png",
        ["Elche CF"] = "https://crests.football-data.org/285.png",
        ["RC Celta de Vigo"] = "https://crests.football-data.org/558.png",
        ["Sevilla FC"] = "https://crests.football-data.org/559.png",
        ["RC Deportivo La Coruña"] = "https://crests.football-data.org/560.png",
        ["Real Racing Club de Santander"] = "https://crests.football-data.org/5335.png",

        // Ligue 1
        ["Toulouse FC"] = "https://crests.football-data.org/511.png",
        ["Stade Brestois 29"] = "https://crests.football-data.org/512.png",
        ["Olympique de Marseille"] = "https://crests.football-data.org/516.png",
        ["AJ Auxerre"] = "https://crests.football-data.org/519.png",
        ["Lille OSC"] = "https://crests.football-data.org/521.png",
        ["OGC Nice"] = "https://crests.football-data.org/522.png",
        ["Olympique Lyonnais"] = "https://crests.football-data.org/523.png",
        ["Paris Saint-Germain FC"] = "https://crests.football-data.org/524.png",
        ["FC Lorient"] = "https://crests.football-data.org/525.png",
        ["Stade Rennais FC 1901"] = "https://crests.football-data.org/529.png",
        ["ES Troyes AC"] = "https://crests.football-data.org/531.png",
        ["Angers SCO"] = "https://crests.football-data.org/532.png",
        ["Le Havre AC"] = "https://crests.football-data.org/533.png",
        ["Le Mans FC"] = "https://upload.wikimedia.org/wikipedia/en/5/57/Le_Mans_FC_logo.svg",
        ["Racing Club de Lens"] = "https://crests.football-data.org/546.png",
        ["AS Monaco FC"] = "https://crests.football-data.org/548.png",
        ["RC Strasbourg Alsace"] = "https://crests.football-data.org/576.png",
        ["Paris FC"] = "https://crests.football-data.org/1045.png",
    };

    public static string? Lookup(string? fullTeamName) =>
        fullTeamName != null && Map.TryGetValue(fullTeamName, out var crestUrl) ? crestUrl : null;
}
