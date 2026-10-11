using System.Text.Json;
using ContourSimplificationDemo;

Preset[] presets = Presets.Load();
if (args.Length == 1 && args[0] == "--check")
{
    DemoChecks.Run(presets);
    return 0;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = "wwwroot"
});
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:5090");
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/presets", () => Results.Json(presets));
app.MapPost("/api/candidates", async (HttpContext context) =>
{
    if (!context.Request.HasJsonContentType())
        return Results.BadRequest(new { error = "Provide an application/json request body." });
    try
    {
        CandidateRequest? request = await context.Request.ReadFromJsonAsync<CandidateRequest>(context.RequestAborted);
        if (request is null) return Results.BadRequest(new { error = "Provide a JSON object with a points array." });
        return Results.Json(CandidateGenerator.Generate(request.Points, context.RequestAborted));
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
    }
    catch (InputException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "Invalid JSON. Provide points as an array of finite [x, y] numbers." });
    }
    catch (BadHttpRequestException)
    {
        return Results.BadRequest(new { error = "Provide a valid application/json request body." });
    }
});
app.Run();
return 0;
