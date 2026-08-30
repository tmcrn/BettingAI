namespace BettingAI.Services;

// Runs inside the API process itself so bet settlement (and therefore
// learning) happens automatically without depending on an external cron
// entry existing for it. Checks every 15 minutes.
public class AutoSettlementBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;

    public AutoSettlementBackgroundService(IServiceScopeFactory scopeFactory)
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
                var settlementService = scope.ServiceProvider.GetRequiredService<BetSettlementService>();
                var settled = await settlementService.SettlePendingBetsAsync(stoppingToken);

                if (settled > 0)
                {
                    Console.WriteLine($"🎯 Auto-settlement: {settled} pari(s) résolu(s), LearningNotebook mis à jour");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Auto-settlement error: {ex.Message}");
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
