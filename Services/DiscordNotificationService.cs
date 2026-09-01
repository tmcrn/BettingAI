using BettingAI.Models;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;

namespace BettingAI.Services;

// Posts betting activity to a Discord channel via webhook. No-ops silently
// if no webhook URL is configured, so it never breaks the app for setups
// that don't want Discord notifications.
public class DiscordNotificationService
{
    private readonly HttpClient _httpClient;
    private readonly string? _webhookUrl;

    public DiscordNotificationService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _webhookUrl = configuration["Discord:WebhookUrl"];
    }

    // Fired when a cycle actually had real matches/odds to look at but ended
    // with zero bets - so the user knows the system is alive and checked,
    // not silently dead. Deliberately NOT fired when the window was simply
    // empty (nothing to check), to avoid pinging every 45min for nothing.
    public Task NotifyNoActionAsync(string reason, int matchesFound, int matchesWithOdds) => SendAsync(new
    {
        embeds = new[]
        {
            new
            {
                title = "🔍 Cycle terminé - aucun pari",
                color = 0x95a5a6, // gris
                fields = new object[]
                {
                    new { name = "Matchs trouvés (fenêtre)", value = matchesFound.ToString(), inline = true },
                    new { name = "Avec cotes réelles", value = matchesWithOdds.ToString(), inline = true },
                    new { name = "Raison", value = reason, inline = false }
                },
                timestamp = DateTime.UtcNow.ToString("o")
            }
        }
    });

    // Fired once per cycle that actually placed something, alongside the
    // per-bet detail notifications - those don't carry how many matches the
    // cycle looked at overall, only NotifyNoActionAsync did, so a cycle that
    // DID bet had no visible match-count context at all.
    public Task NotifyCycleSummaryAsync(int matchesFound, int matchesWithOdds, int betsPlaced) => SendAsync(new
    {
        embeds = new[]
        {
            new
            {
                title = "📋 Résumé du cycle",
                color = 0x3498db,
                fields = new object[]
                {
                    new { name = "Matchs analysés", value = matchesFound.ToString(), inline = true },
                    new { name = "Avec cotes réelles", value = matchesWithOdds.ToString(), inline = true },
                    new { name = "Paris placés", value = betsPlaced.ToString(), inline = true }
                },
                timestamp = DateTime.UtcNow.ToString("o")
            }
        }
    });

    public Task NotifyBetPlacedAsync(Bet bet) => SendAsync(new
    {
        embeds = new[]
        {
            new
            {
                title = "🎯 Nouveau pari placé",
                color = 0x3498db, // bleu
                fields = new object[]
                {
                    new { name = "Match", value = $"{bet.HomeTeam} vs {bet.AwayTeam}", inline = false },
                    new { name = "Type", value = bet.BetType ?? "?", inline = true },
                    new { name = "Mise", value = $"{bet.Stake:0.00}€", inline = true },
                    new { name = "Confiance", value = $"{bet.Confidence:P0}", inline = true },
                    new { name = "Raisonnement", value = string.IsNullOrWhiteSpace(bet.Reasoning) ? "-" : bet.Reasoning, inline = false }
                },
                timestamp = DateTime.UtcNow.ToString("o")
            }
        }
    });

    public Task NotifyBetWonAsync(Bet bet) => SendAsync(new
    {
        embeds = new[]
        {
            new
            {
                title = "✅ Pari gagné !",
                color = 0x2ecc71, // vert
                fields = new object[]
                {
                    new { name = "Match", value = $"{bet.HomeTeam} vs {bet.AwayTeam}", inline = false },
                    new { name = "Type", value = bet.BetType ?? "?", inline = true },
                    new { name = "Mise", value = $"{bet.Stake:0.00}€", inline = true },
                    new { name = "Gains", value = $"{(bet.Winnings ?? 0):0.00}€", inline = true }
                },
                timestamp = DateTime.UtcNow.ToString("o")
            }
        }
    });

    public Task NotifyBetLostAsync(Bet bet) => SendAsync(new
    {
        embeds = new[]
        {
            new
            {
                title = "❌ Pari perdu",
                color = 0xe74c3c, // rouge
                fields = new object[]
                {
                    new { name = "Match", value = $"{bet.HomeTeam} vs {bet.AwayTeam}", inline = false },
                    new { name = "Type", value = bet.BetType ?? "?", inline = true },
                    new { name = "Mise perdue", value = $"{bet.Stake:0.00}€", inline = true }
                },
                timestamp = DateTime.UtcNow.ToString("o")
            }
        }
    });

    // Legs.Count counts LEGS, not distinct matches - wrong for a same-match
    // combo (e.g. HOME_WIN + OVER_GOALS, both legs on ONE match), which used
    // to say "(2 matchs)" for something that's really one match.
    private static int DistinctMatchCount(BetCombo combo) => combo.Legs.Select(l => l.MatchId).Distinct().Count();

    public Task NotifyComboPlacedAsync(BetCombo combo) => SendAsync(new
    {
        embeds = new[]
        {
            new
            {
                title = $"🎰 Nouveau combiné placé ({(DistinctMatchCount(combo) == 1 ? "1 match" : $"{DistinctMatchCount(combo)} matchs")})",
                color = 0x9b59b6, // violet
                fields = BuildComboFields(combo, includeLegResults: false),
                timestamp = DateTime.UtcNow.ToString("o")
            }
        }
    });

    public Task NotifyComboWonAsync(BetCombo combo) => SendAsync(new
    {
        embeds = new[]
        {
            new
            {
                title = "✅ Combiné gagné !",
                color = 0x2ecc71,
                fields = BuildComboFields(combo, includeLegResults: true, extra: new
                {
                    name = "Gains",
                    value = $"{(combo.Winnings ?? 0):0.00}€",
                    inline = true
                }),
                timestamp = DateTime.UtcNow.ToString("o")
            }
        }
    });

    public Task NotifyComboLostAsync(BetCombo combo) => SendAsync(new
    {
        embeds = new[]
        {
            new
            {
                title = "❌ Combiné perdu",
                color = 0xe74c3c,
                fields = BuildComboFields(combo, includeLegResults: true),
                timestamp = DateTime.UtcNow.ToString("o")
            }
        }
    });

    private static object[] BuildComboFields(BetCombo combo, bool includeLegResults, object? extra = null)
    {
        var fields = new List<object>();

        // Group by match - a same-match combo (2+ legs on ONE match, e.g.
        // HOME_WIN + OVER_GOALS) now shows as a single field naming that one
        // match with both types joined ("HOME_WIN @ 2.00 + OVER_GOALS @
        // 2.00"), instead of one field per leg that read as if the combo
        // spanned two different matches when it's really just one.
        foreach (var legGroup in combo.Legs.GroupBy(l => l.MatchId))
        {
            var first = legGroup.First();
            var typesText = string.Join(" + ", legGroup.Select(leg =>
            {
                var legLabel = includeLegResults ? $"{leg.Result switch { "WIN" => "✅", "LOSS" => "❌", _ => "⏳" }} " : "";
                return $"{legLabel}{leg.BetType} @ {leg.Odds:0.00}";
            }));
            fields.Add(new
            {
                name = $"{first.HomeTeam} vs {first.AwayTeam}",
                value = typesText,
                inline = false
            });
        }

        fields.Add(new { name = "Mise", value = $"{combo.Stake:0.00}€", inline = true });
        fields.Add(new { name = "Cote combinée", value = $"{combo.CombinedOdds:0.00}", inline = true });
        if (extra != null) fields.Add(extra);
        if (!string.IsNullOrWhiteSpace(combo.Reasoning))
        {
            fields.Add(new { name = "Raisonnement", value = combo.Reasoning, inline = false });
        }

        return fields.ToArray();
    }

    private async Task SendAsync(object payload)
    {
        if (string.IsNullOrWhiteSpace(_webhookUrl)) return; // Discord non configuré

        try
        {
            var response = await _httpClient.PostAsJsonAsync(_webhookUrl, payload);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"⚠️ Discord notification failed: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Discord notification error: {ex.Message}");
        }
    }
}
