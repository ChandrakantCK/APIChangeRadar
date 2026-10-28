namespace Radar.Api.Endpoints
{
    public static class DiagnosticsEndpoints
    {
        public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder app)
        {
            // Shows callers only their OWN connection details, like "what is my IP" sites do.
            // Handy to confirm what the API sees behind Cloudflare + Render.
            app.MapGet("/diagnostics/client", (HttpContext http) => TypedResults.Ok(new ClientInfo(
                    ClientIp: http.Connection.RemoteIpAddress?.ToString(),
                    Scheme: http.Request.Scheme,
                    OriginalForwardedFor: http.Request.Headers["X-Original-For"].ToString(),
                    UnprocessedForwardedFor: http.Request.Headers["X-Forwarded-For"].ToString())))
                .WithTags("Diagnostics")
                .WithName("GetClientInfo");

            return app;
        }
    }

    public sealed record ClientInfo(string? ClientIp, string Scheme, string OriginalForwardedFor, string UnprocessedForwardedFor);
}