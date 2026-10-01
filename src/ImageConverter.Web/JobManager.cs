using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using ImageConverter.Core;
using ImageConverter.Core.Export;
using ImageConverter.Core.GCode;
using ImageConverter.Core.Spiral;
using ImageConverter.Core.Tsp;

namespace ImageConverter.Web;

public enum JobStatus { Queued, Running, Done, Error, Cancelled }

public sealed class Job
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public required string Name { get; init; }
    public required string Mode { get; init; }          // "tsp" or "spiral"
    public string FileSuffix => Mode == "spiral" ? "spiral" : "tsp";
    public DateTime Created { get; } = DateTime.UtcNow;
    public CancellationTokenSource Cts { get; } = new();
    public Stopwatch Clock { get; } = new();

    public volatile JobStatus Status = JobStatus.Queued;
    public volatile string Message = "Queued…";
    public string? Error;

    // results
    public LineArtResult? Art;
    public GCodeResult? GCode;
    public GCodeSettings? GCodeSettings;
    public byte[]? PathBytes;      // float32 LE: x0,y0,x1,y1,…
    public byte[]? GCodeBytes;
    public string? GCodeHead;

    public object ToStatus() => new
    {
        id = Id,
        mode = Mode,
        status = Status.ToString().ToLowerInvariant(),
        message = Message,
        error = Error,
        elapsedSeconds = Math.Round(Clock.Elapsed.TotalSeconds, 1),
        stats = Status != JobStatus.Done || Art is null || GCode is null ? null : new
        {
            points = Art.Path.Length,
            workingWidth = Art.Width,
            workingHeight = Art.Height,
            drawingWidthMm = Math.Round(GCode.WidthMm, 1),
            drawingHeightMm = Math.Round(GCode.HeightMm, 1),
            lineLengthM = Math.Round(GCode.DrawLengthMm / 1000, 2),
            moves = GCode.Moves,
            estimatedMinutes = Math.Round(GCode.EstimatedTime.TotalMinutes, 1),
            gcodeKb = (GCodeBytes?.Length ?? 0) / 1024,
            gcodeHead = GCodeHead
        }
    };
}

/// <summary>Runs conversions in the background (one at a time – they use all CPU cores) and keeps results for 2 hours.</summary>
public sealed class JobManager : IDisposable
{
    private readonly ConcurrentDictionary<string, Job> _jobs = new();
    private readonly SemaphoreSlim _gate = new(1);
    private readonly Timer _cleanup;
    private readonly ILogger<JobManager> _log;

    public JobManager(ILogger<JobManager> log)
    {
        _log = log;
        _cleanup = new Timer(_ => Cleanup(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public bool TryGet(string id, out Job job) => _jobs.TryGetValue(id, out job!);

    public Job Start(float[] gray, int width, int height, string name, string mode,
                     TspArtSettings tsp, SpiralSettings spiral, GCodeSettings gcode)
    {
        var job = new Job { Name = name, Mode = mode == "spiral" ? "spiral" : "tsp" };
        _jobs[job.Id] = job;
        _ = Task.Run(() => RunAsync(job, gray, width, height, tsp, spiral, gcode));
        return job;
    }

    public void Cancel(string id)
    {
        if (_jobs.TryGetValue(id, out var job)) job.Cts.Cancel();
    }

    private async Task RunAsync(Job job, float[] gray, int width, int height,
                                TspArtSettings tsp, SpiralSettings spiral, GCodeSettings gcode)
    {
        var ct = job.Cts.Token;
        bool entered = false;
        try
        {
            await _gate.WaitAsync(ct);
            entered = true;

            job.Status = JobStatus.Running;
            job.Clock.Start();
            var progress = new ActionProgress(m => job.Message = m);

            var art = job.Mode == "spiral"
                ? SpiralGenerator.Generate(gray, width, height, spiral, progress, ct)
                : TspArtGenerator.Generate(gray, width, height, tsp, progress, ct);

            job.Message = "Writing G-code…";
            var g = GCodeWriter.Write(art.Path, art.Width, art.Height, gcode, job.Name);

            var bytes = new byte[art.Path.Length * 8];
            for (int i = 0; i < art.Path.Length; i++)
            {
                BitConverter.TryWriteBytes(bytes.AsSpan(i * 8), art.Path[i].X);
                BitConverter.TryWriteBytes(bytes.AsSpan(i * 8 + 4), art.Path[i].Y);
            }

            job.Art = art;
            job.GCode = g;
            job.GCodeSettings = gcode;
            job.PathBytes = bytes;
            job.GCodeBytes = Encoding.UTF8.GetBytes(g.Code);
            job.GCodeHead = string.Join('\n', g.Code.Split('\n').Take(24)) + "\n…";
            job.Message = "Done";
            job.Status = JobStatus.Done;
        }
        catch (OperationCanceledException)
        {
            job.Message = "Cancelled";
            job.Status = JobStatus.Cancelled;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Job {Id} failed", job.Id);
            job.Error = ex.Message;
            job.Message = "Failed";
            job.Status = JobStatus.Error;
        }
        finally
        {
            job.Clock.Stop();
            if (entered) _gate.Release();
        }
    }

    public static string Svg(Job job, double strokeMm) =>
        SvgWriter.Write(job.Art!.Path, job.GCode!.WidthMm, job.GCode.HeightMm,
                        job.GCodeSettings?.KeepAspectRatio ?? true, strokeMm);

    private void Cleanup()
    {
        foreach (var (id, job) in _jobs)
        {
            bool finished = job.Status is JobStatus.Done or JobStatus.Error or JobStatus.Cancelled;
            if (finished && DateTime.UtcNow - job.Created > TimeSpan.FromHours(2))
                _jobs.TryRemove(id, out _);
        }
    }

    public void Dispose()
    {
        _cleanup.Dispose();
        foreach (var job in _jobs.Values) job.Cts.Cancel();
    }

    private sealed class ActionProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
