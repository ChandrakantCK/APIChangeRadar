using Radar.Infrastructure;
using Radar.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseHttpsRedirection();
}


app.MapGet("/", () => Results.Ok(new { name = "API Change Radar", status = "running" }))
   .WithName("GetRoot");

app.MapHealthChecks("/health");
app.MapAuthEndpoints();

app.Run();