using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BettingAI.Services;

// Shared plumbing for every AI call in this app (bet decisions, score
// pronos, screenshot OCR) - calls Google's Gemini API (free tier) instead
// of a locally-run Ollama model. Replaces Ollama entirely: running it
// locally meant a whole second machine (a Mac, just to host the model)
// that went quietly offline for two weeks without anyone noticing (the
// daily cycle failing with a plain connection error each time), and even
// when it worked, the PC alone couldn't carry the model's own RAM
// footprint. Gemini runs entirely on Google's infrastructure, so this app
// just makes a plain HTTPS call and needs no local inference compute at
// all, on either machine - no Mac to remember to turn on, no local model
// eating the PC's RAM.
//
// Free tier (gemini-flash-latest, Sept 2026): ~1500 requests/day, 15/
// minute - real headroom over a full cycle's worst case (~80 calls: 40
// matches x 2, see DecideBetsEndpoint). Also natively multimodal (one
// model does both vision and text), unlike the old Ollama setup which
// needed two separate models (OLLAMA_MODEL text-only + a separate
// OLLAMA_VISION_MODEL for screenshots).
//
// Get a free key (no credit card) at https://aistudio.google.com/apikey
// and set GEMINI_API_KEY. Privacy note (Google's own terms): the free
// tier is normally eligible to be used to improve Google's products
// (human review possible) - UNLESS the caller is in the EEA, Switzerland
// or the UK, where the free tier gets the same no-training guarantee as
// the paid tier.
public static class GeminiService
{
    private static readonly string ApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
    private static readonly string Model = Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-flash-latest";
    private static readonly string BaseUrl = Environment.GetEnvironmentVariable("GEMINI_BASE_URL") ?? "https://generativelanguage.googleapis.com/v1beta";

    public static async Task<string> ImageToBase64Async(IFormFile file, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return Convert.ToBase64String(ms.ToArray());
    }

    // One real call to Gemini - prompt only, or prompt + one image
    // (base64, no data: prefix, plus its real MIME type). generationConfig.
    // responseMimeType "application/json" forces syntactically valid JSON
    // out of the model (still whatever shape the prompt itself asks for -
    // no schema enforced here), which alone fixes most of what the old
    // Ollama setup needed a manual "retry if it returned prose" loop for.
    // Callers still do their own "is this the shape I actually asked for"
    // check on the result - that's response-specific, not this method's job.
    //
    // Retries a 429 (rate limit - the free tier's 15/minute cap is the one
    // real headroom concern during a burst of per-match calls) and a bare
    // connection failure with a short backoff, same reasoning as the old
    // Ollama retry loop this replaces: transient, not a reason to lose that
    // match's decision for the rest of the cycle.
    public static async Task<string> AskAsync(string prompt, CancellationToken ct, string? imageBase64 = null, string imageMimeType = "image/jpeg")
    {
        if (string.IsNullOrEmpty(ApiKey))
        {
            throw new Exception("GEMINI_API_KEY non configurée - crée une clé gratuite sur https://aistudio.google.com/apikey et règle la variable d'environnement");
        }

        var parts = new List<object> { new { text = prompt } };
        if (imageBase64 != null)
        {
            parts.Add(new { inline_data = new { mime_type = imageMimeType, data = imageBase64 } });
        }

        var payload = new
        {
            contents = new[] { new { parts = parts.ToArray() } },
            generationConfig = new { responseMimeType = "application/json" }
        };

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        const int maxAttempts = 3;
        var retryDelay = TimeSpan.FromSeconds(10);

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await client.PostAsJsonAsync(
                    $"{BaseUrl}/models/{Model}:generateContent?key={ApiKey}",
                    payload,
                    cancellationToken: ct);
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                await Task.Delay(retryDelay, ct);
                continue;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxAttempts)
            {
                await Task.Delay(retryDelay, ct);
                continue;
            }

            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                // A missing/invalid key, or a model name that doesn't exist,
                // fails right here with Gemini's own error text - surfaced
                // as-is rather than a generic HTTP error, since the fix (a
                // real key, or GEMINI_MODEL pointed at a real model) is
                // right there in the response body.
                throw new Exception($"Erreur Gemini ({(int)response.StatusCode}): {body}");
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                // No candidates usually means the safety filters blocked the
                // request/response - promptFeedback carries the actual
                // reason when present, surfaced instead of a bare "empty
                // response" that gives no hint why.
                var feedback = doc.RootElement.TryGetProperty("promptFeedback", out var pf) ? pf.GetRawText() : body;
                throw new Exception($"Gemini n'a renvoyé aucune réponse exploitable: {feedback}");
            }

            var textPart = candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
            return (textPart ?? "").Trim();
        }
    }
}
