namespace BettingAI.Services;

// Replaces dependency on an external cron entry (none exists in this repo)
// with an in-process loop, same pattern as AutoSettlementBackgroundService.
// Runs every 45 minutes and only looks at matches kicking off within the
// next hour - real odds are rarely posted much earlier than that anyway,
// and OddsScraperService now refuses to fabricate odds for matches too far
// out, so there's little point checking further ahead.
public class AutoDecideBetsBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    private const int WindowHours = 1;

    private readonly IHttpClientFactory _httpClientFactory;

    public AutoDecideBetsBackgroundService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var response = await client.PostAsync(
                    $"http://localhost:5255/api/auto-decide-bets?windowHours={WindowHours}",
                    content: null,
                    stoppingToken);
                var body = await response.Content.ReadAsStringAsync(stoppingToken);
                Console.WriteLine($"🤖 Auto-decide cycle (window {WindowHours}h): {body}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Auto-decide-bets background error: {ex.Message}");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // shutting down
            }
        }
    }
}
