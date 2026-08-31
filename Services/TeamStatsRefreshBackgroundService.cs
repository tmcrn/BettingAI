namespace BettingAI.Services;

// Keeps TeamStats current automatically - form/xG-proxy/fatigue drift every
// matchday, so a one-time seed would go stale within a week. Runs once a
// day; the window now spans ~2 seasons (TeamStatsSeedingService.DaysBack)
// so this takes several minutes (dozens of paced, rate-limited requests to
// football-data.org) rather than the single quick call a 45-day window used
// to need - still cheap for a once-a-day background job.
public class TeamStatsRefreshBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(3);

    private readonly IServiceScopeFactory _scopeFactory;

    public TeamStatsRefreshBackgroundService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
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
                using var scope = _scopeFactory.CreateScope();
                var seedingService = scope.ServiceProvider.GetRequiredService<TeamStatsSeedingService>();
                var (success, message, teamsUpdated) = await seedingService.SeedFromRealMatchesAsync(stoppingToken);

                Console.WriteLine(success
                    ? $"📊 TeamStats refresh: {message}"
                    : $"⚠️ TeamStats refresh skipped: {message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ TeamStats refresh error: {ex.Message}");
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
