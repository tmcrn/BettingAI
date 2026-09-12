using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BettingAI.Services;

// Shared plumbing for OcrOddsEndpoint (single ticket) and
// OcrBatchOddsEndpoint (many tickets from one capture/PDF page) - calls a
// local Ollama VISION model to read a bet-slip screenshot directly, server
// side, instead of the old client-side Tesseract.js OCR.
//
// That Tesseract.js approach turned out unreliable in practice: a heavy WASM
// bundle whose worker script AND language pack are both fetched from a CDN
// (jsdelivr) the browser sometimes blocks or fails to load entirely - a
// well-documented failure mode (see naptha/tesseract.js issues #961, #901,
// #444) - and even when it did load, raw OCR text off a stylized bookmaker
// app screenshot (colored backgrounds, custom fonts, a stacked mobile UI
// splitting a label and its value onto separate lines) needed fragile
// regex/line-position heuristics (parseOddsFromOcrText, matchNameScore) to
// ever find the right number. A vision-capable LLM reads the image AND
// understands "find the number next to this label" in one step, and runs
// entirely server side - no CDN/CSP failure mode at all.
//
// Requires a multimodal model pulled locally first (Ollama doesn't
// auto-pull - "ollama pull qwen2.5vl"). qwen2.5vl is the default: public
// benchmarks consistently rank it above llava-class models specifically for
// screenshots/small text/structured layouts at the same size, which is
// exactly this use case. Overridable via OLLAMA_VISION_MODEL, same pattern
// as DecideBets.cs's OLLAMA_MODEL for the text-only model used everywhere
// else in this app (that one can't see images at all).
public static class OllamaVisionService
{
    private static readonly string VisionModel = Environment.GetEnvironmentVariable("OLLAMA_VISION_MODEL") ?? "qwen2.5vl";

    public static async Task<string> ImageToBase64Async(IFormFile file, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return Convert.ToBase64String(ms.ToArray());
    }

    // Same idea as CallOllamaWithRetryAsync in DecideBets.cs, minus the
    // retry loop (a screenshot import is a manual, one-off user action -
    // if it fails, the raw text is shown for a human to read/retry, not a
    // silent unattended cycle) - a 3-minute timeout is real headroom for a
    // single vision call on modest local hardware.
    public static async Task<string> AskAsync(string prompt, string imageBase64, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(
                "http://localhost:11434/api/generate",
                new { model = VisionModel, prompt, images = new[] { imageBase64 }, stream = false },
                cancellationToken: ct);
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"Ollama injoignable ({ex.Message}) - vérifie qu'il tourne (ollama serve)");
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            // A vision model that was never pulled fails right here with a
            // clear "model not found" from Ollama itself - surfaced as-is
            // instead of a generic HTTP error, since fixing it needs one
            // "ollama pull {VisionModel}", not a code change.
            throw new Exception($"Modèle vision '{VisionModel}' indisponible ({(int)response.StatusCode}) - lance 'ollama pull {VisionModel}' puis réessaie. Détail: {body}");
        }

        var doc = JsonDocument.Parse(body);
        return (doc.RootElement.GetProperty("response").GetString() ?? "").Trim();
    }

    // Pulls the first decimal-looking number (1-3 digits, 1-2 decimals) out
    // of the model's reply - it's asked to answer with ONLY the number, but
    // this trims around it defensively rather than trusting that literally
    // (a model can still wrap it in a stray word or punctuation).
    public static decimal? ExtractOdds(string raw)
    {
        var m = Regex.Match(raw, @"(\d{1,3}[.,]\d{1,2})");
        if (!m.Success) return null;
        return decimal.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }
}
