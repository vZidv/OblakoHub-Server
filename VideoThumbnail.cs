using System.ComponentModel;
using System.Diagnostics;

internal static class VideoThumbnail
{
    public static async Task<byte[]?> ReadAsync(string file, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("ffmpeg")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "-nostdin", "-loglevel", "quiet",
            "-protocol_whitelist", "file", "-f", "mov", "-threads", "1", "-i", file,
            "-map", "0:V:0", "-frames:v", "1",
            "-vf", "scale=128:128:force_original_aspect_ratio=decrease",
            "-threads", "1", "-c:v", "png", "-f", "image2pipe", "pipe:1"
        })
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                return null;
        }
        catch (Win32Exception)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var output = new MemoryStream();
        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0 && output.Length > 0 ? output.ToArray() : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(); }
                catch (InvalidOperationException) when (process.HasExited) { }
                await process.WaitForExitAsync();
            }
        }
    }
}
