using BettingAI.Services;
using FastEndpoints;

namespace BettingAI.Endpoints;

public class GetCycleStatusResponse
{
    public bool HasRunYet { get; set; }
    public DateTime? LastRunAtUtc { get; set; }
    public double? MinutesSinceLastRun { get; set; }
    public string? LastOutcome { get; set; }
    public int MatchesFound { get; set; }
    public int BetsPlaced { get; set; }
    public string? LastMessage { get; set; }
}

// Lets you check whether the 45-min auto-decide-bets cron is actually
// alive without digging through journalctl - most cycles legitimately
// find nothing in the 1h window (outside match hours) and that case sends
// no Discord notification on purpose, which otherwise looks identical to
// a stuck/dead service from the outside.
public class GetCycleStatusEndpoint : EndpointWithoutRequest<GetCycleStatusResponse>
{
    private readonly CycleStatusService _cycleStatus;

    public GetCycleStatusEndpoint(CycleStatusService cycleStatus)
    {
        _cycleStatus = cycleStatus;
    }

    public override void Configure()
    {
        Get("/api/cycle-status");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await Send.OkAsync(new GetCycleStatusResponse
        {
            HasRunYet = _cycleStatus.LastRunAt != null,
            LastRunAtUtc = _cycleStatus.LastRunAt,
            MinutesSinceLastRun = _cycleStatus.LastRunAt == null
                ? null
                : Math.Round((DateTime.UtcNow - _cycleStatus.LastRunAt.Value).TotalMinutes, 1),
            LastOutcome = _cycleStatus.LastOutcome,
            MatchesFound = _cycleStatus.MatchesFound,
            BetsPlaced = _cycleStatus.BetsPlaced,
            LastMessage = _cycleStatus.LastMessage
        });
    }
}
