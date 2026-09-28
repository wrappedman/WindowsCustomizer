using System.Diagnostics;
using System.Text;
using WindowsCustomizer.Core.Logging;

namespace WindowsCustomizer.Core.Commands;

/// <summary>
/// Command Execution Layer (spec 41). All external tools (DISM, oscdimg, reg.exe, PowerShell)
/// go through this runner. Arguments are passed as an array (never concatenated into a raw command
/// string) to eliminate injection. Every run is logged with exit code and duration.
/// </summary>
public sealed record CommandRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    TimeSpan? Timeout = null,
    bool RequireElevation = false)
{
    public string Display =>
        FileName + " " + string.Join(" ", Arguments.Select(QuoteForDisplay));

    private static string QuoteForDisplay(string a) =>
        a.Contains(' ') ? "\"" + a + "\"" : a;
}

public sealed record CommandResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    TimeSpan Duration,
    bool TimedOut,
    bool Cancelled)
{
    public bool Success => ExitCode == 0 && !TimedOut && !Cancelled;
}

public interface IProcessRunner
{
    Task<CommandResult> RunAsync(
        CommandRequest request,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Safe process runner with timeout, cancellation, output capture and logging.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    private readonly IAppLogger _log;

    public ProcessRunner(IAppLogger log)
    {
        _log = log;
    }

    public async Task<CommandResult> RunAsync(
        CommandRequest request,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        var start = Stopwatch.StartNew();
        _log.Info("Process", $"RUN: {request.Display}");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var timedOut = false;
        var cancelled = false;
        int exitCode = -1;

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = request.WorkingDirectory ?? Environment.CurrentDirectory
        };

        // ArgumentList is safe: each argument is quoted by the runtime, no string concatenation.
        foreach (var arg in request.Arguments)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (stdout)
            {
                stdout.AppendLine(e.Data);
            }

            output?.Report(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (stderr)
            {
                stderr.AppendLine(e.Data);
            }

            output?.Report(e.Data);
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start '{request.FileName}'.");
            }
        }
        catch (Exception ex)
        {
            _log.Error("Process", $"Failed to start {request.FileName}: {ex.Message}");
            throw;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        if (request.Timeout is { } timeout && timeout > TimeSpan.Zero)
        {
            timeoutCts.CancelAfter(timeout);
        }

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = !timeoutCts.IsCancellationRequested;
            timedOut = timeoutCts.IsCancellationRequested;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // process may have exited already
            }

            try
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        exitCode = process.HasExited ? process.ExitCode : -1;
        start.Stop();

        var result = new CommandResult(
            exitCode,
            stdout.ToString(),
            stderr.ToString(),
            start.Elapsed,
            timedOut,
            cancelled);

        if (result.Success)
        {
            _log.Info("Process", $"OK ({exitCode}) in {start.Elapsed.TotalSeconds:0.0}s: {request.FileName}");
        }
        else if (cancelled)
        {
            _log.Warn("Process", $"CANCELLED: {request.FileName}");
        }
        else if (timedOut)
        {
            _log.Error("Process", $"TIMEOUT: {request.FileName}", (Exception?)null);
        }
        else
        {
            _log.Warn("Process", $"EXIT {exitCode}: {request.FileName}", result.StdErr.Length > 0 ? result.StdErr : null);
        }

        return result;
    }
}
