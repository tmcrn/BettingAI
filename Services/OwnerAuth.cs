using Microsoft.AspNetCore.Http;

namespace BettingAI.Services;

// Guards every mutating endpoint (anything that writes to the DB, calls
// Mistral, or burns football-data.org/Sofascore quota) now that the app
// can be reached by someone other than its owner - a guest link shared
// over a tunnel (see the README/setup notes) gets read-only access to the
// GET endpoints (portfolio, learning notebook, ...) but every POST/DELETE
// endpoint requires this token.
//
// Read from the OWNER_TOKEN environment variable rather than
// appsettings.json - same "never committed" handling as the football-data
// API key, but without needing IConfiguration wired into every mutating
// endpoint's constructor just for this one check.
//
// Empty/unset OWNER_TOKEN means auth is deliberately OFF (the default,
// and exactly how this app behaved before this existed) - set a real,
// long random value in your own environment before ever sharing a link
// that reaches this app from outside your own network.
public static class OwnerAuth
{
    private static readonly string? OwnerToken = Environment.GetEnvironmentVariable("OWNER_TOKEN");

    public static bool IsAuthorized(HttpContext ctx) =>
        string.IsNullOrEmpty(OwnerToken) || ctx.Request.Headers["X-Owner-Token"] == OwnerToken;

    // Several endpoints call other guarded endpoints of THIS SAME APP over
    // a real loopback HTTP request (e.g. AutoDecideBets -> DecideBets) -
    // those calls need this attached too, or they'd get rejected by the
    // very guard this class enforces. Deliberately NOT solved by trusting
    // the caller's remote IP being 127.0.0.1 instead: once this app is
    // reached through a tunnel (ngrok/Cloudflare Tunnel/...), the tunnel
    // client itself connects to this app over loopback, so a genuine
    // outside guest's request would ALSO arrive looking like it came from
    // 127.0.0.1 - trusting loopback would silently defeat the whole guard
    // the moment a tunnel is involved.
    public static void AttachSelfCallToken(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(OwnerToken)) request.Headers.Add("X-Owner-Token", OwnerToken);
    }
}
