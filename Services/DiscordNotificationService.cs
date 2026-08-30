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
