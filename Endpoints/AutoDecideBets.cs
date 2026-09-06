using FastEndpoints;
using BettingAI.Services;
using BettingAI.Data;
using System.Text.Json;

namespace BettingAI.Endpoints;

public class AutoDecideResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public List<BetData>? Bets { get; set; }
}

public class BetData
{
    public string? Match { get; set; }
    public string? BetType { get; set; }
    public decimal Stake { get; set; }
    public decimal Confidence { get; set; }
    public string? Reasoning { get; set; }
}

public class AutoDecideBetsRequest
{
    // Optional override for manual/test calls. Omitted (the normal case,
    // including the once-daily background cron), it defaults to "the rest
    // of today" (hours until midnight Europe/Paris) - see HoursUntilEndOfDayParis.
    [QueryParam]
    public int? WindowHours { get; set; }

    // Start of the window in hours from now (default 0 = "now"). Set to
    // e.g. 72 with WindowHours=96 to run a cycle only on matches between
    // 72h and 96h from now - useful to test a later slice of the schedule
    // without re-touching matches an earlier cycle already bet on.
    [QueryParam]
    public int? MinHours { get; set; }
}

public class AutoDecideBetsEndpoint : Endpoint<AutoDecideBetsRequest, AutoDecideResponse>
{
    private readonly HttpClient _httpClient;
    private readonly OddsScraperService _scraper;
    private readonly BettingContext _db;
    private readonly DiscordNotificationService _discord;
    private readonly CycleStatusService _cycleStatus;

    public AutoDecideBetsEndpoint(HttpClient httpClient, OddsScraperService scraper, BettingContext db, DiscordNotificationService discord, CycleStatusService cycleStatus)
    {
        _httpClient = httpClient;
        _scraper = scraper;
        _db = db;
        _discord = discord;
        _cycleStatus = cycleStatus;
    }

    public override void Configure()
    {
        Post("/api/auto-decide-bets");
        AllowAnonymous();
    }

    public override async Task HandleAsync(AutoDecideBetsRequest req, CancellationToken ct)
    {
        if (!OwnerAuth.IsAuthorized(HttpContext))
        {
            HttpContext.Response.StatusCode = 403;
            return;
        }

        try
        {
            Console.WriteLine("🤖 AUTO-DECIDE-BETS STARTED");

            // The default HttpClient.Timeout (100s) used to be plenty for the old
            // one-call-per-match design. Now that decide-bets makes TWO Ollama
            // calls per match (OUTCOME + GOALS), a batch of 8-9 matches routinely
            // takes well over 100s end to end - confirmed live: "The request was
            // canceled due to the configured HttpClient.Timeout of 100 seconds
            // elapsing" on a real cycle, silently killing the whole batch instead
            // of just one slow call. Must be set before this client makes its
            // first request this instance (HttpClient throws if changed after).
            //
            // Raised from 10 to 60 minutes after switching the default local
            // model to qwen2.5:32b-instruct - confirmed live at ~35-47s per
            // Ollama call (vs ~7s for the 7B model), two calls per match. A
            // full daily cycle can see 15-25 matches (30-50 calls), which at
            // worst-case ~45s/call is ~35-40 minutes - 10 minutes was no
            // longer enough margin.
            _httpClient.Timeout = TimeSpan.FromMinutes(60);

            // 0️⃣ Règle d'abord les paris d'hier (ou de plus tôt aujourd'hui) avant de
            // décider les nouveaux - le LearningNotebook reflète les vrais résultats
            // les plus récents au moment où l'IA raisonne, plutôt que d'attendre le
            // prochain passage du service de règlement automatique (15min).
            try
            {
                var settleRequest = new HttpRequestMessage(HttpMethod.Post, "http://localhost:5255/api/settle-pending-bets");
                OwnerAuth.AttachSelfCallToken(settleRequest);
                var settleResp = await _httpClient.SendAsync(settleRequest, ct);
                var settleBody = await settleResp.Content.ReadAsStringAsync(ct);
                Console.WriteLine($"🎯 Pré-règlement avant décision: {settleBody}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Pré-règlement échoué (on continue quand même): {ex.Message}");
            }

            // 1️⃣ Récupère les matchs du reste de la journée (heure de Paris) par défaut -
            // ce cycle tourne une fois par jour et doit couvrir toute la journée en un
            // seul passage, pas juste une fenêtre glissante étroite. windowHours reste
            // surchargeable manuellement (bouton "Forcer un cycle" du dashboard, tests).
            var windowHours = req.WindowHours ?? HoursUntilEndOfDayParis();
            var minHours = req.MinHours ?? 0;
            var upcomingMatches = await GetUpcomingMatches(windowHours, minHours);
            if (upcomingMatches == null || upcomingMatches.Count == 0)
            {
                // No Discord notification here on purpose (an empty window is the
                // normal case most cycles, outside match hours - notifying every
                // time would be spam) - but that means this is the ONE outcome
                // with zero external visibility, which reads as "the cron is
                // broken" from the outside. CycleStatusService/GetCycleStatus
                // exists so that can be checked on demand instead of guessed at.
                _cycleStatus.Record("no_matches", 0, 0, $"Aucun match entre {minHours}h et {windowHours}h qui suivent");
                await Send.OkAsync(new AutoDecideResponse
                {
                    Success = false,
                    Message = "No upcoming matches found"
                });
                return;
            }

            Console.WriteLine($"✓ Found {upcomingMatches.Count} upcoming matches");

            // 2️⃣ Pour chaque match, tente de scraper les cotes - mais garde le
            // match même sans cotes réelles: DecideBets sait encore proposer
            // des paris "stats seules" (BTTS, over/under) dessus à partir de
            // TeamStats, sans avoir besoin de cotes 1X2. Exclure ces matchs
            // ici les aurait empêché d'atteindre l'IA pour ce type de pari.
            // Limite haute de sécurité, pas une vraie limite métier - avec un cycle
            // quotidien couvrant toute la journée sur 5 championnats, on peut
            // facilement avoir 15-25 matchs. À noter: Mistral tourne ici avec une
            // fenêtre de contexte de 4096 tokens (voir la config Ollama) - un trop
            // grand nombre de matchs dans un seul prompt (analyse + cotes détaillées
            // par match) peut la dépasser et dégrader/tronquer la réponse. À surveiller
            // si des cycles à forte affluence de matchs produisent des réponses
            // visiblement incomplètes.
            var matchesWithOdds = new List<dynamic>();
            var matchesWithRealOdds = 0;
            foreach (var match in upcomingMatches.Take(25))
            {
                Console.WriteLine($"  Scraping odds for: {match.HomeTeam} vs {match.AwayTeam}");

                var odds = await _scraper.GetSofascoreOdds(match.HomeTeam, match.AwayTeam);
                if (odds != null) matchesWithRealOdds++;

                matchesWithOdds.Add(new
                {
                    match.Id,
                    match.HomeTeam,
                    match.AwayTeam,
                    match.UtcDate,
                    match.HomeTeamShort,
                    match.AwayTeamShort,
                    match.HomeTeamCrest,
                    match.AwayTeamCrest,
                    match.CompetitionCode,
                    odds // null si pas encore publiées - DecideBets gère ce cas
                });
                await Task.Delay(1000); // Rate limit respectueux
            }

            Console.WriteLine($"✓ {matchesWithOdds.Count} matchs à analyser ({matchesWithRealOdds} avec cotes réelles)");

            // 3️⃣ Appelle decide-bets avec les matchs trouvés
            // Same formula as GetPortfolio: 10 + gains réglés - TOUTES les mises
            // (y compris les paris encore PENDING). L'ancien calcul ne comptait
            // que les gains réglés, donc l'IA voyait toujours ~10€ de marge même
            // avec des dizaines d'euros déjà engagés sur des paris en attente.
            var settledWinnings = (_db.Bets.Where(b => b.Result != "PENDING").Sum(b => b.Winnings) ?? 0)
                + (_db.BetCombos.Where(c => c.Result != "PENDING").Sum(c => c.Winnings) ?? 0);
            var totalStaked = _db.Bets.Sum(b => b.Stake) + _db.BetCombos.Sum(c => c.Stake);
            var balance = 10 + settledWinnings - totalStaked;

            // 🔧 UTILISER LES INDICES comme IDs
            var decideBetsPayload = new
            {
                currentBalance = balance,
                matches = matchesWithOdds.Select((m, index) => new  // ← index!
                {
                    id = index.ToString(),  // ← "0", "1", "2"... pour que l'IA le retrouve de façon fiable
                    realMatchId = (string)m.Id,  // ← vrai ID, conservé pour le règlement automatique
                    homeTeam = m.HomeTeam,
                    awayTeam = m.AwayTeam,
                    utcDate = m.UtcDate,
                    // Carried through so DecideBets' sourceMatch (and the Bet/
                    // ComboLeg it saves) actually has a flag/crest to show -
                    // see the comment on GetUpcomingMatches() below.
                    homeTeamShort = (string?)m.HomeTeamShort,
                    awayTeamShort = (string?)m.AwayTeamShort,
                    homeTeamCrest = (string?)m.HomeTeamCrest,
                    awayTeamCrest = (string?)m.AwayTeamCrest,
                    competitionCode = (string?)m.CompetitionCode
                }).ToList(),
                bettingHistory = (object?)null
            };

            // Real loopback HTTP call (not an in-process method call), so
            // it hits DecideBetsEndpoint's own OwnerAuth guard like any
            // other request would - see AttachSelfCallToken.
            var decideBetsRequest = new HttpRequestMessage(HttpMethod.Post, "http://localhost:5255/api/decide-bets")
            {
                Content = JsonContent.Create(decideBetsPayload)
            };
            OwnerAuth.AttachSelfCallToken(decideBetsRequest);
            var response = await _httpClient.SendAsync(decideBetsRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                await Send.OkAsync(new AutoDecideResponse
                {
                    Success = false,
                    Message = "Failed to get AI decision"
                });
                return;
            }

            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine("🤖 AI DECISION:");
            Console.WriteLine(content);

            // 4️⃣ Parse et retourne les paris
            var doc = JsonDocument.Parse(content);
            var bets = new List<BetData>();

            if (doc.RootElement.TryGetProperty("bets", out var betsElement))
            {
                foreach (var bet in betsElement.EnumerateArray())
                {
                    var betType = bet.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

                    if (betType == "COMBO")
                    {
                        // Combos don't carry a top-level matchId (they span several
                        // matches via "legs") - already saved by decide-bets, just
                        // summarize them differently here.
                        bets.Add(new BetData
                        {
                            Match = "Combiné",
                            BetType = "COMBO",
                            Stake = bet.TryGetProperty("stake", out var s) ? s.GetDecimal() : 0,
                            Confidence = bet.TryGetProperty("confidence", out var c) ? c.GetDecimal() : 0,
                            Reasoning = bet.TryGetProperty("reasoning", out var r) ? r.GetString() : null
                        });
                        continue;
                    }

                    // DecideBets now echoes back what it actually PERSISTED (real
                    // homeTeam/awayTeam included directly), not the AI's raw proposal
                    // matched by index against matchesWithOdds - a bet rejected as a
                    // duplicate or over the balance floor simply isn't in this list at
                    // all anymore, instead of misreporting as placed.
                    var homeTeam = bet.TryGetProperty("homeTeam", out var htEl) ? htEl.GetString() : null;
                    var awayTeam = bet.TryGetProperty("awayTeam", out var atEl) ? atEl.GetString() : null;

                    bets.Add(new BetData
                    {
                        Match = $"{homeTeam} vs {awayTeam}",
                        BetType = betType,
                        Stake = bet.GetProperty("stake").GetDecimal(),
                        Confidence = bet.GetProperty("confidence").GetDecimal(),
                        Reasoning = bet.GetProperty("reasoning").GetString()
                    });
                }
            }

            if (bets.Count == 0)
            {
                // decide-bets' AnalysisUsed carries its debugLog, including "NO JSON
                // ARRAY IN RESPONSE" when Mistral derailed entirely (returned prose
                // instead of the requested JSON, even after a retry) rather than
                // actually evaluating the matches - confirmed live. The reason text
                // below used to always claim "no bet met the AI's criteria" regardless
                // of which of these actually happened, which is a real explanation
                // fabricated for a failure that has nothing to do with the stats.
                var analysisUsed = doc.RootElement.TryGetProperty("analysisUsed", out var auEl) ? auEl.GetString() ?? "" : "";
                var malformedResponse = analysisUsed.Contains("NO JSON ARRAY IN RESPONSE");

                var reason = malformedResponse
                    ? "L'IA n'a pas répondu dans le format attendu (même après une relance) - aucune décision n'a donc pu être prise ce cycle, indépendamment des stats des matchs"
                    : matchesWithRealOdds > 0
                        ? "Matchs analysés (dont certains avec cotes réelles), mais aucun pari ne remplissait les critères de l'IA"
                        : "Matchs analysés sur stats seules (aucune cote réelle publiée pour l'instant), mais aucun pari ne remplissait les critères de l'IA";
                await _discord.NotifyNoActionAsync(reason, upcomingMatches.Count, matchesWithRealOdds);
                _cycleStatus.Record(malformedResponse ? "ai_malformed_response" : "no_bets", upcomingMatches.Count, 0, reason);
            }
            else
            {
                _cycleStatus.Record("bets_placed", upcomingMatches.Count, bets.Count, $"{bets.Count} pari(s) placé(s)");
                await _discord.NotifyCycleSummaryAsync(upcomingMatches.Count, matchesWithRealOdds, bets.Count);
            }

            await Send.OkAsync(new AutoDecideResponse
            {
                Success = true,
                Message = $"✅ IA a décidé {bets.Count} pari(s) automatiquement !",
                Bets = bets
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            _cycleStatus.Record("error", 0, 0, ex.Message);
            await Send.OkAsync(new AutoDecideResponse
            {
                Success = false,
                Message = ex.Message
            });
        }
    }

    // Hours remaining until midnight Europe/Paris from right now - what "the
    // rest of today" means for a cycle that runs once a day. Same fallback
    // logic as AutoDecideBetsBackgroundService if tzdata is unavailable.
    private static int HoursUntilEndOfDayParis()
    {
        TimeZoneInfo parisTz;
        try
        {
            parisTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        }
        catch
        {
            parisTz = TimeZoneInfo.CreateCustomTimeZone("FallbackParis", TimeSpan.FromHours(1), "Fallback Paris", "UTC+1");
        }

        var nowParis = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, parisTz);
        var midnightParis = nowParis.Date.AddDays(1);
        var hours = (midnightParis - nowParis).TotalHours;
        return Math.Max(1, (int)Math.Ceiling(hours));
    }

    private async Task<List<dynamic>?> GetUpcomingMatches(int windowHours, int minHours = 0)
    {
        try
        {
            // Appelle l'endpoint interne
            var response = await _httpClient.GetAsync($"http://localhost:5255/api/matches/upcoming?windowHours={windowHours}&minHours={minHours}");

            if (!response.IsSuccessStatusCode) return null;

            var content = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(content);

            var matches = new List<dynamic>();
            foreach (var match in doc.RootElement.EnumerateArray())
            {
                matches.Add(new
                {
                    Id = match.GetProperty("id").GetString(),
                    HomeTeam = match.GetProperty("homeTeam").GetString(),
                    AwayTeam = match.GetProperty("awayTeam").GetString(),
                    UtcDate = match.GetProperty("utcDate").GetString(),
                    // These 5 used to stop here entirely, so every bet placed
                    // through this pipeline always ended up with a null
                    // CompetitionCode/HomeTeamCrest/AwayTeamCrest/HomeTeamShort/
                    // AwayTeamShort - the dashboard's flag and crest images never
                    // had anything to render, even though /api/matches/upcoming
                    // itself (FootballMatch) already carries all of them.
                    HomeTeamShort = match.TryGetProperty("homeTeamShort", out var hts) ? hts.GetString() : null,
                    AwayTeamShort = match.TryGetProperty("awayTeamShort", out var ats) ? ats.GetString() : null,
                    HomeTeamCrest = match.TryGetProperty("homeTeamCrest", out var htc) ? htc.GetString() : null,
                    AwayTeamCrest = match.TryGetProperty("awayTeamCrest", out var atc) ? atc.GetString() : null,
                    CompetitionCode = match.TryGetProperty("competitionCode", out var cc) ? cc.GetString() : null
                });
            }

            return matches;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching matches: {ex.Message}");
            return null;
        }
    }
}