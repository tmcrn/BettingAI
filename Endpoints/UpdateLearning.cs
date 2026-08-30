using BettingAI.Data;
using BettingAI.Models;
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

    public UpdateLearningEndpoint(BettingContext context)
    {
        _context = context;
    }

    public override void Configure()
    {
        Post("/api/update-learning");
        AllowAnonymous();
    }

    public override async Task HandleAsync(UpdateLearningRequest req, CancellationToken ct)
    {
        var notebook = await _context.LearningNotebook.FirstOrDefaultAsync(cancellationToken: ct);

        if (notebook == null)
        {
            notebook = new LearningNotebook
            {
                CreatedAt = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                TotalBets = 0,
                WonBets = 0,
                WinRate = 0,
                AverageConfidence = 0
            };
            _context.LearningNotebook.Add(notebook);
        }

        // Update stats
        notebook.LastUpdated = DateTime.UtcNow;
        var bets = await _context.Bets.ToListAsync(cancellationToken: ct);
        notebook.TotalBets = bets.Count;
        notebook.WonBets = bets.Count(b => b.Result == "WIN");
        notebook.WinRate = bets.Count > 0 ? (decimal)notebook.WonBets / bets.Count : 0;
        notebook.AverageConfidence = bets.Count > 0 ? bets.Average(b => b.Confidence) : 0;

        await _context.SaveChangesAsync(ct);

        await Send.OkAsync(new UpdateLearningResponse
        {
            Success = true,
            Message = $"Learning updated: {notebook.WonBets}/{notebook.TotalBets} wins ({notebook.WinRate:P})"
        });
    }
}