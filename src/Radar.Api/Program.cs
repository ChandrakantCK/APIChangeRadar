using Radar.Infrastructure;
using Radar.Api.Endpoints;
using Microsoft.AspNetCore.HttpOverrides;
using Radar.Api.Authentication;
using Radar.Api.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJwtAuthentication();
builder.Services.AddApiRateLimiting(builder.Configuration);

// Render's proxy terminates HTTPS and forwards the request. These headers carry the real client IP and
// the original scheme. Render's proxy addresses aren't fixed, so we trust whatever proxy is directly in
// front of us - but only ONE hop (ForwardLimit = 1, the default), so values a client adds itself are ignored.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// First: everything after this sees the real client IP and scheme.
app.UseForwardedHeaders();

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

app.Run();