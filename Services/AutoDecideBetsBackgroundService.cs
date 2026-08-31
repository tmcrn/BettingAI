namespace BettingAI.Services;

// Replaces dependency on an external cron entry (none exists in this repo)
// with an in-process loop, same pattern as AutoSettlementBackgroundService.
// Runs ONCE A DAY at 8:00 Paris time - early enough to cover the whole
// day's matches in one shot, and late enough that yesterday's matches
// (even late kickoffs) are long finished and settled by the time we ask.
// AutoDecideBetsEndpoint itself calls /api/settle-pending-bets first so the
// LearningNotebook reflects yesterday's results before deciding today's
// bets, then decides on every match kicking off the rest of today (see its
// default windowHours computation) rather than a narrow rolling window.
public class AutoDecideBetsBackgroundService : BackgroundService
{
    private static readonly TimeSpan DailyRunTimeLocal = new(8, 0, 0); // 8:00 Europe/Paris

    private static readonly TimeZoneInfo ParisTimeZone = ResolveParisTimeZone();

    private readonly IHttpClientFactory _httpClientFactory;

    public AutoDecideBetsBackgroundService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun();
            Console.WriteLine($"📅 Next daily auto-decide cycle in {delay:hh\\hmm} (target: {DateTime.UtcNow.Add(delay):yyyy-MM-dd HH:mm} UTC)");

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            try
            {
                var client = _httpClientFactory.CreateClient();
                var response = await client.PostAsync(
                    "http://localhost:5255/api/auto-decide-bets",
                    content: null,
                    stoppingToken);
                var body = await response.Content.ReadAsStringAsync(stoppingToken);
                Console.WriteLine($"📅 Daily auto-decide cycle: {body}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Auto-decide-bets background error: {ex.Message}");
            }

            // Sleep a minute past the run before recomputing the next target -
            // guards against firing twice if ExecuteAsync somehow loops faster
            // than the HTTP call took (it won't in practice, just cheap safety).
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private static TimeSpan TimeUntilNextRun()
    {
        var nowUtc = DateTime.UtcNow;
        var nowParis = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, ParisTimeZone);
        var todayRunParis = nowParis.Date + DailyRunTimeLocal;
        var nextRunParis = nowParis < todayRunParis ? todayRunParis : todayRunParis.AddDays(1);
        var nextRunUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(nextRunParis, DateTimeKind.Unspecified), ParisTimeZone);

        var delay = nextRunUtc - nowUtc;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    // Linux ships the IANA tz database so "Europe/Paris" normally resolves
    // fine, but fall back to a fixed UTC+1 zone (no DST) rather than crash
    // the whole service if tzdata is ever missing - close enough to keep the
    // daily cycle firing roughly on time instead of not firing at all.
    private static TimeZoneInfo ResolveParisTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Could not resolve Europe/Paris timezone ({ex.Message}) - falling back to fixed UTC+1");
            return TimeZoneInfo.CreateCustomTimeZone("FallbackParis", TimeSpan.FromHours(1), "Fallback Paris (UTC+1, no DST)", "UTC+1");
        }
    }
}
