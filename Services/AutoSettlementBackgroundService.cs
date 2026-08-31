namespace BettingAI.Services;

// No longer registered in Program.cs - the user wants settlement grouped
// into the single 8h daily cycle rather than checked every 15 minutes
// around the clock (AutoDecideBetsEndpoint already calls
// /api/settle-pending-bets as its first step). Left here rather than
// deleted in case continuous settlement is ever wanted again; the actual
// settlement logic it calls into (BetSettlementService) is unchanged and
// still used by the daily cycle and by manual settlement.
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
