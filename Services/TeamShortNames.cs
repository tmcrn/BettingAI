namespace BettingAI.Services;

// Fallback for FootballDataService.GetUpcomingMatchesAsync when football-data.org's
// own "shortName" field is missing or null for a team (happens occasionally, and
// happened for every match placed before the AutoDecideBets pipeline was fixed to
// actually carry HomeTeamShort/AwayTeamShort through to a saved Bet/ComboLeg at
// all - see the fix in AutoDecideBets.cs). Keyed by the exact official name
// football-data.org returns as "name", which is also what's stored as HomeTeam/
// AwayTeam on Bet/ComboLeg - never used for stats lookups, display only, same
// rule as HomeTeamShort itself (see the comment on FootballMatch.HomeTeamShort).
//
// These are common media shortenings, not guaranteed to be byte-for-byte what
// football-data.org's own shortName would have returned for a given team (that
// field isn't publicly documented) - good enough for a readable ticket, not a
// source of truth for anything else.
public static class TeamShortNames
{
    private static readonly Dictionary<string, string> Map = new()
    {
        // Ligue 1
        ["Paris Saint-Germain FC"] = "PSG",
        ["Olympique de Marseille"] = "Marseille",
        ["Olympique Lyonnais"] = "Lyon",
        ["AS Monaco FC"] = "Monaco",
        ["LOSC Lille"] = "Lille",
        ["OGC Nice"] = "Nice",
        ["Stade Rennais FC 1901"] = "Rennes",
        ["RC Strasbourg Alsace"] = "Strasbourg",
        ["RC Lens"] = "Lens",
        ["Stade Brestois 29"] = "Brest",
        ["FC Nantes"] = "Nantes",
        ["Toulouse FC"] = "Toulouse",
        ["Montpellier HSC"] = "Montpellier",
        ["Angers SCO"] = "Angers",
        ["ES Troyes AC"] = "Troyes",
        ["Le Havre AC"] = "Le Havre",
        ["FC Metz"] = "Metz",
        ["Clermont Foot 63"] = "Clermont",
        ["Paris FC"] = "Paris FC",
        ["AJ Auxerre"] = "Auxerre",

        // Premier League
        ["Manchester City FC"] = "Man City",
        ["Manchester United FC"] = "Man United",
        ["Liverpool FC"] = "Liverpool",
        ["Chelsea FC"] = "Chelsea",
        ["Arsenal FC"] = "Arsenal",
        ["Tottenham Hotspur FC"] = "Tottenham",
        ["Newcastle United FC"] = "Newcastle",
        ["Aston Villa FC"] = "Aston Villa",
        ["West Ham United FC"] = "West Ham",
        ["Brighton & Hove Albion FC"] = "Brighton",
        ["Everton FC"] = "Everton",
        ["Fulham FC"] = "Fulham",
        ["Crystal Palace FC"] = "Crystal Palace",
        ["Brentford FC"] = "Brentford",
        ["Nottingham Forest FC"] = "Nott'm Forest",
        ["Wolverhampton Wanderers FC"] = "Wolves",
        ["AFC Bournemouth"] = "Bournemouth",
        ["Sunderland AFC"] = "Sunderland",
        ["Leeds United FC"] = "Leeds",
        ["Burnley FC"] = "Burnley",

        // La Liga
        ["Real Madrid CF"] = "Real Madrid",
        ["FC Barcelona"] = "Barcelona",
        ["Club Atlético de Madrid"] = "Atlético Madrid",
        ["Athletic Club"] = "Athletic Club",
        ["Real Sociedad de Fútbol"] = "Real Sociedad",
        ["Real Betis Balompié"] = "Real Betis",
        ["Villarreal CF"] = "Villarreal",
        ["Valencia CF"] = "Valencia",
        ["Sevilla FC"] = "Sevilla",
        ["RCD Espanyol de Barcelona"] = "Espanyol",
        ["CA Osasuna"] = "Osasuna",
        ["Deportivo Alavés"] = "Alavés",
        ["RC Celta de Vigo"] = "Celta Vigo",
        ["Getafe CF"] = "Getafe",
        ["Rayo Vallecano de Madrid"] = "Rayo Vallecano",
        ["RCD Mallorca"] = "Mallorca",
        ["Girona FC"] = "Girona",
        ["Levante UD"] = "Levante",
        ["Real Valladolid CF"] = "Valladolid",
        ["Málaga CF"] = "Málaga",
        ["RC Deportivo La Coruña"] = "Deportivo La Coruña",

        // Serie A
        ["FC Internazionale Milano"] = "Inter",
        ["AC Milan"] = "Milan",
        ["Juventus FC"] = "Juventus",
        ["SSC Napoli"] = "Napoli",
        ["AS Roma"] = "Roma",
        ["SS Lazio"] = "Lazio",
        ["ACF Fiorentina"] = "Fiorentina",
        ["Atalanta BC"] = "Atalanta",
        ["Bologna FC 1909"] = "Bologna",
        ["Torino FC"] = "Torino",
        ["Udinese Calcio"] = "Udinese",
        ["Genoa CFC"] = "Genoa",
        ["US Sassuolo Calcio"] = "Sassuolo",
        ["Hellas Verona FC"] = "Verona",
        ["Cagliari Calcio"] = "Cagliari",
        ["Parma Calcio 1913"] = "Parma",
        ["AC Monza"] = "Monza",
        ["Frosinone Calcio"] = "Frosinone",
        ["Venezia FC"] = "Venezia",
        ["Como 1907"] = "Como",
        ["Empoli FC"] = "Empoli",
        ["US Lecce"] = "Lecce",
        ["US Cremonese"] = "Cremonese",
        ["Pisa Sporting Club"] = "Pisa",

        // Bundesliga
        ["FC Bayern München"] = "Bayern München",
        ["Borussia Dortmund"] = "Dortmund",
        ["Bayer 04 Leverkusen"] = "Leverkusen",
        ["RB Leipzig"] = "RB Leipzig",
        ["Eintracht Frankfurt"] = "Eintracht Frankfurt",
        ["VfB Stuttgart"] = "Stuttgart",
        ["Borussia Mönchengladbach"] = "Gladbach",
        ["VfL Wolfsburg"] = "Wolfsburg",
        ["SC Freiburg"] = "Freiburg",
        ["1. FC Union Berlin"] = "Union Berlin",
        ["1. FSV Mainz 05"] = "Mainz 05",
        ["TSG 1899 Hoffenheim"] = "Hoffenheim",
        ["FC Augsburg"] = "Augsburg",
        ["Werder Bremen"] = "Werder Bremen",
        ["1. FC Heidenheim 1846"] = "Heidenheim",
        ["VfL Bochum 1848"] = "Bochum",
        ["Holstein Kiel"] = "Holstein Kiel",
        ["FC St. Pauli 1910"] = "St. Pauli",
        ["SC Paderborn 07"] = "Paderborn",
        ["SV 07 Elversberg"] = "Elversberg",
        ["Hamburger SV"] = "Hamburger SV",
    };

    // Null (not the full name) when no entry exists - callers already fall
    // back to the full name in that case (same TeamName() pattern used in
    // GetPortfolio.cs), so this never needs a "did we find one" flag.
    public static string? Lookup(string? fullTeamName) =>
        fullTeamName != null && Map.TryGetValue(fullTeamName, out var shortName) ? shortName : null;
}
