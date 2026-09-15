using BettingAI.Services;
using FastEndpoints;
using System.Text.Json;

namespace BettingAI.Endpoints;

public class OcrBatchOddsRequest
{
    public IFormFile Image { get; set; } = null!;

    // JSON array of candidate match names, exactly as the frontend's own
    // /api/portfolio?resultFilter=PENDING list shows them (e.g.
    // ["PSG vs Marseille", "Lyon vs Nice"]) - a plain text field carrying
    // JSON rather than a real repeated-field list, to not depend on exactly
    // how FastEndpoints' multipart binding handles multi-value form fields.
    public string MatchNamesJson { get; set; } = "[]";
}

public class OcrBatchOddsMatch
{
    // Position in the MatchNamesJson list this result is for - indices
    // instead of asking the model to echo the match name back verbatim
    // (same "give the AI a numbered id, not free text" trick already used
    // in AutoDecideBetsEndpoint for exactly this reliability reason).
    public int Index { get; set; }
    public decimal Odds { get; set; }
}

public class OcrBatchOddsResponse
{
    public bool Success { get; set; }
    public List<OcrBatchOddsMatch> Results { get; set; } = new();
    public string? RawText { get; set; }
    public string? Message { get; set; }
}

// Reads odds for MANY tickets at once off one uploaded image (one page of a
// capture/PDF - the frontend still renders a PDF's pages to images with
// pdf.js, that part always worked fine, and calls this endpoint once per
// page) via a local Ollama vision model. Replaces the old client-side
// Tesseract.js batch import (runBatchOddsImport in wwwroot/index.html) -
// see OllamaVisionService for why.
public class OcrBatchOddsEndpoint : Endpoint<OcrBatchOddsRequest, OcrBatchOddsResponse>
{
    public override void Configure()
    {
        Post("/api/ocr-batch-odds");
        AllowAnonymous();
        AllowFileUploads();
    }

    public override async Task HandleAsync(OcrBatchOddsRequest req, CancellationToken ct)
    {
        if (!OwnerAuth.IsAuthorized(HttpContext))
        {
            HttpContext.Response.StatusCode = 403;
            return;
        }

        if (req.Image == null || req.Image.Length == 0)
        {
            await Send.OkAsync(new OcrBatchOddsResponse { Success = false, Message = "❌ Aucune image reçue" });
            return;
        }

        // An iPhone photo (as opposed to a screenshot) is HEIC by default -
        // Safari's own file picker normally re-encodes it to JPEG before
        // upload, but a raw .heic can still get through (e.g. picked via
        // the Fichiers app instead of Photos, or "Conserver l'original" in
        // Réglages > Appareil photo > Formats). Ollama's vision models
        // can't decode HEIC and this app has no HEIC->JPEG conversion of
        // its own, so this fails clearly up front instead of Ollama
        // silently returning garbage off an image it couldn't read.
        if (IsHeic(req.Image))
        {
            await Send.OkAsync(new OcrBatchOddsResponse
            {
                Success = false,
                Message = "❌ Format HEIC non supporté - dans Réglages > Appareil photo > Formats, choisis \"Le plus compatible\" (ou renvoie la photo via Messages/Mail, qui la convertit automatiquement en JPEG)."
            });
            return;
        }

        List<string>? matchNames;
        try
        {
            matchNames = JsonSerializer.Deserialize<List<string>>(req.MatchNamesJson);
        }
        catch (JsonException)
        {
            matchNames = null;
        }
        if (matchNames == null || matchNames.Count == 0)
        {
            await Send.OkAsync(new OcrBatchOddsResponse { Success = false, Message = "❌ Aucun ticket en cours à mettre à jour" });
            return;
        }

        try
        {
            var base64 = await OllamaVisionService.ImageToBase64Async(req.Image, ct);
            var numberedList = string.Join("\n", matchNames.Select((m, i) => $"{i}: {m}"));
            var prompt =
                "Tu vois une capture d'écran (ou une page) d'un ou plusieurs tickets de pari sportif (application de bookmaker type Winamax). " +
                "Voici une liste numérotée de matchs qui nous intéressent - et RIEN D'AUTRE:\n" + numberedList + "\n\n" +
                "Pour CHAQUE match de cette liste qui apparaît clairement dans l'image, retrouve sa propre cote " +
                "(le nombre décimal, généralement entre 1 et 20, situé dans le bloc de CE match précis) - PAS le " +
                "\"cote totale\"/\"mise\" global d'un ticket qui regrouperait plusieurs matchs différents ensemble. " +
                "Réponds STRICTEMENT en JSON : un tableau d'objets {\"index\": <numéro de la liste ci-dessus>, \"odds\": <nombre>}, " +
                "sans aucun texte avant ou après. N'inclus que les matchs identifiés avec certitude. " +
                "Si aucun match de la liste n'apparaît dans l'image, réponds : []";

            var raw = await OllamaVisionService.AskAsync(prompt, base64, ct);
            var results = ParseBatchResults(raw, matchNames.Count);

            await Send.OkAsync(new OcrBatchOddsResponse { Success = true, Results = results, RawText = raw });
        }
        catch (Exception ex)
        {
            await Send.OkAsync(new OcrBatchOddsResponse { Success = false, Message = ex.Message });
        }
    }

    // Checks both the browser-reported content type and the filename
    // extension - a raw .heic picked via the Fichiers app can arrive with
    // a generic "application/octet-stream" content type instead of a
    // proper image/heic one, so the extension is the more reliable signal
    // in practice.
    private static bool IsHeic(IFormFile file) =>
        file.ContentType.Contains("heic", StringComparison.OrdinalIgnoreCase) ||
        file.ContentType.Contains("heif", StringComparison.OrdinalIgnoreCase) ||
        file.FileName.EndsWith(".heic", StringComparison.OrdinalIgnoreCase) ||
        file.FileName.EndsWith(".heif", StringComparison.OrdinalIgnoreCase);

    // Extracts the {index, odds} JSON array from the model's reply - same
    // "find the outermost [ ... ]" tolerance as CallOllamaWithRetryAsync in
    // DecideBets.cs, since a vision model can still wrap its JSON in prose
    // despite being told not to. Drops any entry with an out-of-range index
    // or an implausible odds value rather than failing the whole batch over
    // one bad entry.
    private static List<OcrBatchOddsMatch> ParseBatchResults(string raw, int matchCount)
    {
        var start = raw.IndexOf('[');
        var end = raw.LastIndexOf(']');
        if (start == -1 || end <= start) return new List<OcrBatchOddsMatch>();

        var json = raw.Substring(start, end - start + 1);
        try
        {
            var doc = JsonDocument.Parse(json);
            var results = new List<OcrBatchOddsMatch>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (!el.TryGetProperty("index", out var idxEl) || !el.TryGetProperty("odds", out var oddsEl)) continue;
                if (idxEl.ValueKind != JsonValueKind.Number || oddsEl.ValueKind != JsonValueKind.Number) continue;

                // GetInt32() throws on a non-integer token (a model returning
                // "index": 2.0 instead of 2, say) - go through double first
                // so one oddly-formatted entry doesn't drop the whole batch.
                var idx = (int)idxEl.GetDouble();
                var odds = oddsEl.GetDecimal();
                if (idx < 0 || idx >= matchCount || odds < 1) continue;
                results.Add(new OcrBatchOddsMatch { Index = idx, Odds = odds });
            }
            return results;
        }
        catch (JsonException)
        {
            return new List<OcrBatchOddsMatch>();
        }
    }
}
