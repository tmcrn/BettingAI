using FastEndpoints;
using System.Diagnostics;

namespace BettingAI.Endpoints;

public class OllamaStatusResponse
{
    // "active" / "inactive" / "failed" (real systemctl states), "unknown"
    // (couldn't parse), "unsupported" (not Linux/systemd - e.g. Mac dev),
    // or "error" (the systemctl call itself failed - see Message).
    public string? Status { get; set; }
    public string? Message { get; set; }
}

// Lets the dashboard start/stop the Ollama systemd service on demand (e.g.
// from a phone over Tailscale) - so it doesn't have to run 24/7 eating
// several GB of RAM just in case, but can still be spun up right before a
// manual cycle and shut back down after. Linux/systemd only: a clear
// "unsupported" response on any other OS, since this codebase is shared
// between the Mac (dotnet run, no systemd) and the tower (systemd).
public class GetOllamaStatusEndpoint : EndpointWithoutRequest<OllamaStatusResponse>
{
    public override void Configure()
    {
        Get("/api/ollama/status");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await Send.OkAsync(await OllamaControl.RunSystemctlAsync("is-active", requiresSudo: false, ct));
    }
}

public class StartOllamaEndpoint : EndpointWithoutRequest<OllamaStatusResponse>
{
    public override void Configure()
    {
        Post("/api/ollama/start");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await Send.OkAsync(await OllamaControl.RunSystemctlAsync("start", requiresSudo: true, ct));
    }
}

public class StopOllamaEndpoint : EndpointWithoutRequest<OllamaStatusResponse>
{
    public override void Configure()
    {
        Post("/api/ollama/stop");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await Send.OkAsync(await OllamaControl.RunSystemctlAsync("stop", requiresSudo: true, ct));
    }
}

internal static class OllamaControl
{
    public static async Task<OllamaStatusResponse> RunSystemctlAsync(string action, bool requiresSudo, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux())
        {
            return new OllamaStatusResponse
            {
                Status = "unsupported",
                Message = "Contrôle Ollama non disponible sur cette plateforme (Linux/systemd uniquement)."
            };
        }

        try
        {
            // start/stop need root (systemctl); is-active (status check) doesn't.
            // start/stop require a NOPASSWD sudoers entry scoped to exactly
            // "systemctl start/stop ollama" for the service's user - see deploy
            // notes. Without it this returns a clear error instead of hanging
            // on a password prompt that can never be answered here.
            var psi = requiresSudo
                ? new ProcessStartInfo("sudo") { ArgumentList = { "systemctl", action, "ollama" } }
                : new ProcessStartInfo("systemctl") { ArgumentList = { action, "ollama" } };
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;

            using var process = Process.Start(psi);
            if (process == null)
            {
                return new OllamaStatusResponse { Status = "error", Message = "Impossible de lancer systemctl." };
            }

            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            // is-active exits non-zero for "inactive"/"failed" too - that's a
            // real status to report, not a failed call, so read stdout either way.
            if (action == "is-active")
            {
                var reported = stdout.Trim();
                return new OllamaStatusResponse { Status = string.IsNullOrEmpty(reported) ? "unknown" : reported };
            }

            if (process.ExitCode != 0)
            {
                return new OllamaStatusResponse
                {
                    Status = "error",
                    Message = string.IsNullOrWhiteSpace(stderr) ? $"systemctl {action} a échoué (code {process.ExitCode})." : stderr.Trim()
                };
            }

            return new OllamaStatusResponse
            {
                Status = action == "start" ? "active" : "inactive",
                Message = action == "start" ? "Ollama démarré." : "Ollama arrêté."
            };
        }
        catch (Exception ex)
        {
            return new OllamaStatusResponse { Status = "error", Message = ex.Message };
        }
    }
}
