using System.Text;
using System.Text.Json;
using ImageConverter.Core.GCode;
using ImageConverter.Core.Tsp;
using ImageConverter.Web;
using Microsoft.AspNetCore.Http.Features;

// ImageConverter web app:
//   the browser decodes the image to grey-scale and uploads it,
//   the server computes the single-line TSP art + G-code,
//   the browser previews the line (zoom/pan) and offers G-code / SVG / PNG downloads.

var builder = WebApplication.CreateBuilder(args);

const long MaxUploadBytes = 512L * 1024 * 1024;   // grey-scale bytes of a ~500 MP image
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = MaxUploadBytes);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = MaxUploadBytes);
builder.Services.AddSingleton<JobManager>();

var app = builder.Build();
app.UseDefaultFiles();
// "no-cache" = the browser must revalidate (cheap 304 via ETag) on every load, so index.html,
// app.js and app.css always come from the same version after an update.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache"
});

var jsonIn = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

// appsettings.json "TspDefaults" overrides the code defaults (TspDefaults.cs).
// Read on every request, so edits take effect without restarting (just reload the page).
var codeDefaults = (TspDefaults.AutoPixelsPerPoint, TspDefaults.AutoMinPoints, TspDefaults.AutoMaxPoints);
void ApplyTspDefaults()
{
    var section = app.Configuration.GetSection("TspDefaults");
    TspDefaults.AutoPixelsPerPoint = Math.Max(1, section.GetValue("AutoPixelsPerPoint", codeDefaults.Item1));
    TspDefaults.AutoMinPoints = Math.Max(2, section.GetValue("AutoMinPoints", codeDefaults.Item2));
    TspDefaults.AutoMaxPoints = Math.Max(2, section.GetValue("AutoMaxPoints", codeDefaults.Item3));
}
ApplyTspDefaults();

// All parameters with labels, descriptions and defaults -> the web form is generated from this.
app.MapGet("/api/schema", () =>
{
    ApplyTspDefaults();
    return Results.Ok(SettingsSchema.Describe());
});

// Start a conversion. multipart/form-data: gray (w*h bytes, 0..255), width, height, name, settings (JSON)
app.MapPost("/api/jobs", async (HttpRequest request, JobManager jobs) =>
{
    ApplyTspDefaults();
    var form = await request.ReadFormAsync();
    if (!int.TryParse(form["width"], out int width) || !int.TryParse(form["height"], out int height) ||
        width <= 0 || height <= 0)
        return Results.BadRequest(new { error = "width/height missing or invalid" });

    var file = form.Files.GetFile("gray");
    if (file is null || file.Length != (long)width * height)
        return Results.BadRequest(new { error = $"expected {width * (long)height} grey-scale bytes" });

    JobSettings settings;
    try
    {
        settings = JsonSerializer.Deserialize<JobSettings>(form["settings"].ToString(), jsonIn) ?? new();
    }
    catch (JsonException ex)
    {
        return Results.BadRequest(new { error = "invalid settings: " + ex.Message });
    }

    var tsp = settings.Tsp ?? new TspArtSettings();
    var gcode = settings.GCode ?? new GCodeSettings();
    if (string.IsNullOrWhiteSpace(gcode.PenDownCommand))
        return Results.BadRequest(new { error = "The 'pen down' command must not be empty." });
    if (gcode.WidthMm <= 0 || gcode.HeightMm <= 0)
        return Results.BadRequest(new { error = "Drawing width and height must be greater than 0 mm." });

    var bytes = new byte[file.Length];
    await using (var s = file.OpenReadStream())
        await s.ReadExactlyAsync(bytes);

    var gray = new float[bytes.Length];
    for (int i = 0; i < bytes.Length; i++) gray[i] = bytes[i];

    string name = Path.GetFileNameWithoutExtension(form["name"].ToString());
    if (string.IsNullOrWhiteSpace(name)) name = "image";

    var job = jobs.Start(gray, width, height, name, tsp, gcode);
    return Results.Ok(new { id = job.Id });
}).DisableAntiforgery();

app.MapGet("/api/jobs/{id}", (string id, JobManager jobs) =>
    jobs.TryGet(id, out var job) ? Results.Ok(job.ToStatus()) : Results.NotFound());

app.MapDelete("/api/jobs/{id}", (string id, JobManager jobs) =>
{
    jobs.Cancel(id);
    return Results.NoContent();
});

// Path points for the browser preview: float32 little-endian x,y pairs (working-image pixels).
app.MapGet("/api/jobs/{id}/path", (string id, JobManager jobs) =>
    jobs.TryGet(id, out var job) && job.PathBytes is { } b
        ? Results.Bytes(b, "application/octet-stream")
        : Results.NotFound());

app.MapGet("/api/jobs/{id}/gcode", (string id, JobManager jobs) =>
    jobs.TryGet(id, out var job) && job.GCodeBytes is { } b
        ? Results.File(b, "text/plain", $"{job.Name}_tsp.gcode")
        : Results.NotFound());

app.MapGet("/api/jobs/{id}/svg", (string id, double? strokeMm, JobManager jobs) =>
    jobs.TryGet(id, out var job) && job.Status == JobStatus.Done
        ? Results.File(Encoding.UTF8.GetBytes(JobManager.Svg(job, Math.Clamp(strokeMm ?? 0.3, 0.01, 10))),
                       "image/svg+xml", $"{job.Name}_tsp.svg")
        : Results.NotFound());

app.Run();
