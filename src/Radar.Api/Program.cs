using Radar.Infrastructure;
using Radar.Api.Endpoints;
using Radar.Api.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJwtAuthentication();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseHttpsRedirection();
}


app.MapGet("/", () => Results.Ok(new { name = "API Change Radar", status = "running" }))
   .WithName("GetRoot");

// Who is calling? (reads the Bearer token) - then: are they allowed? Order matters.
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapMeEndpoints();

app.Run();