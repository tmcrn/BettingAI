using BettingAI.Services;
using FastEndpoints;

namespace BettingAI.Endpoints;

public class OcrOddsRequest
{
    public IFormFile Image { get; set; } = null!;
    public bool IsCombo { get; set; }
}

public class OcrOddsResponse
{
    public bool Success { get; set; }
    public decimal? Odds { get; set; }
    public string? RawText { get; set; }
    public string? Message { get; set; }
}

// Reads a single ticket's "cote"/"cote totale" off an uploaded bet-slip
// screenshot via a local Ollama vision model - replaces the old client-side
// Tesseract.js single-ticket import (runOddsShotOcr in wwwroot/index.html).
// See OllamaVisionService for why that approach was dropped.
public class OcrOddsEndpoint : Endpoint<OcrOddsRequest, OcrOddsResponse>
{
    public override void Configure()
    {
        Post("/api/ocr-odds");
        AllowAnonymous();
        AllowFileUploads();
    }

    public override async Task HandleAsync(OcrOddsRequest req, CancellationToken ct)
    {
        if (!OwnerAuth.IsAuthorized(HttpContext))
        {
            HttpContext.Response.StatusCode = 403;
            return;
        }

        if (req.Image == null || req.Image.Length == 0)
        {
            await Send.OkAsync(new OcrOddsResponse { Success = false, Message = "❌ Aucune image reçue" });
            return;
        }

        try
        {
            var base64 = await OllamaVisionService.ImageToBase64Async(req.Image, ct);
            var label = req.IsCombo ? "\"cote totale\"" : "\"cote\" (PAS \"cote totale\")";
            var prompt =
                "Tu vois une capture d'écran d'un ticket de pari sportif (application de bookmaker type Winamax). " +
                $"Trouve le nombre affiché à côté du libellé {label} - un multiplicateur décimal, généralement entre 1 et 20 (ex: 2.29). " +
                "Réponds UNIQUEMENT avec ce nombre au format X.XX, sans aucun autre texte, symbole ou explication. " +
                "Si tu ne trouves aucun nombre correspondant précisément à ce libellé, réponds exactement: AUCUN";
            var raw = await OllamaVisionService.AskAsync(prompt, base64, ct);
            var odds = raw.Trim().Equals("AUCUN", StringComparison.OrdinalIgnoreCase)
                ? null
                : OllamaVisionService.ExtractOdds(raw);

            await Send.OkAsync(new OcrOddsResponse { Success = true, Odds = odds, RawText = raw });
        }
        catch (Exception ex)
        {
            await Send.OkAsync(new OcrOddsResponse { Success = false, Message = ex.Message });
        }
    }
}
