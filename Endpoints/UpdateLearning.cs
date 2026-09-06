using BettingAI.Data;
using BettingAI.Services;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Endpoints;

public class UpdateLearningRequest
{
    public int BetId { get; set; }
    public string? Result { get; set; }  // WIN ou LOSS
}

public class UpdateLearningResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public class UpdateLearningEndpoint : Endpoint<UpdateLearningRequest, UpdateLearningResponse>
{
    private readonly BettingContext _context;
    private readonly BetSettlementService _settlementService;

    public UpdateLearningEndpoint(BettingContext context, BetSettlementService settlementService)
    {
        _context = context;
        _settlementService = settlementService;
    }

    public override void Configure()
    {
        Post("/api/update-learning");
        AllowAnonymous();
    }

    public override async Task HandleAsync(UpdateLearningRequest req, CancellationToken ct)
    {
        if (!OwnerAuth.IsAuthorized(HttpContext))
        {
            HttpContext.Response.StatusCode = 403;
            return;
        }

        await _settlementService.RefreshLearningNotebookAsync(ct);

        var notebook = await _context.LearningNotebook
            .OrderByDescending(n => n.LastUpdated)
            .FirstAsync(cancellationToken: ct);

        await Send.OkAsync(new UpdateLearningResponse
        {
            Success = true,
            Message = $"Learning updated: {notebook.WonBets}/{notebook.TotalBets} wins ({notebook.WinRate:P})"
        });
    }
}