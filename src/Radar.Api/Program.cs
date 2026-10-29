using Radar.Infrastructure;
using Radar.Api.Endpoints;
using Radar.Api.Networking;
using Radar.Api.Authentication;
using Radar.Api.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJwtAuthentication();
builder.Services.AddGitHubLogin(builder.Configuration);
builder.Services.AddApiRateLimiting(builder.Configuration);

// Behind Cloudflare + Render: recover the real client IP and https scheme from the proxies' headers.
builder.Services.AddTrustedForwardedHeaders();

// Calls that come through the web app's /api proxy (identified by a shared secret).
builder.Services.AddTrustedWebProxy(builder.Configuration);

var app = builder.Build();

// First: everything after this sees the real client IP and scheme.
app.UseForwardedHeaders();
app.UseTrustedWebProxy();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseHttpsRedirection();
}

app.MapGet("/", () => Results.Ok(new { name = "API Change Radar", status = "running" }))
   .WithName("GetRoot");

app.UseRateLimiter();

// Who is calling? (reads the Bearer token) - then: are they allowed? Order matters.
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapOrganizationEndpoints();
app.MapDiagnosticsEndpoints();

app.Run();