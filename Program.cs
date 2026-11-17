using SkiaSharp;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = null);
var app = builder.Build();
var data = Path.GetFullPath(builder.Configuration["OBLAKO_DATA"] ?? "data");
var files = Directory.CreateDirectory(Path.Combine(data, "files")).FullName;
var credentialsPath = Path.Combine(data, "credentials.json");
var changes = new SemaphoreSlim(1);
var thumbnailSlot = new SemaphoreSlim(1);
var pairingCode = RandomNumberGenerator.GetHexString(32);
var pairingExpires = DateTimeOffset.UtcNow.AddMinutes(15);
var credentials = ReadCredentials();
var tokenHash = credentials?.TokenHash;
long? quotaBytes = credentials?.QuotaBytes;

if (tokenHash is null)
    Console.WriteLine($"Код подключения (15 минут): {pairingCode}");

app.MapGet("/api/v1/health", () => Results.Ok(new { apiVersion = 1 }));
app.MapPost("/api/v1/pair/storage", async (PairRequest request) =>
{
    await changes.WaitAsync();
    try
    {
        return CanPair(request.Code) ? Results.Ok(GetStorage()) : Results.Unauthorized();
    }
    finally
    {
        changes.Release();
    }
});

app.MapPost("/api/v1/pair", async (PairRequest request) =>
{
    await changes.WaitAsync();
    try
    {
        if (!CanPair(request.Code))
            return Results.Unauthorized();

        var storage = GetStorage();
        if (request.QuotaBytes is long limit &&
            (limit <= 0 || limit < storage.UsedBytes || limit > storage.UsedBytes + storage.AvailableBytes))
            return Results.BadRequest(new { error = "Лимит должен покрывать занятый объём и помещаться на диске." });

        var token = RandomNumberGenerator.GetHexString(64);
        var hash = Hash(token);
        await File.WriteAllTextAsync(credentialsPath + ".tmp", JsonSerializer.Serialize(new StoredCredentials(hash, request.QuotaBytes)));
        File.Move(credentialsPath + ".tmp", credentialsPath, overwrite: true);
        quotaBytes = request.QuotaBytes;
        Volatile.Write(ref tokenHash, hash);
        return Results.Ok(new { token });
    }
    finally
    {
        changes.Release();
    }
});

var api = app.MapGroup("/api/v1/files");
api.AddEndpointFilter(async (context, next) =>
{
    var auth = context.HttpContext.Request.Headers.Authorization.ToString();
    var hash = Volatile.Read(ref tokenHash);
    if (hash == null || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
        !Matches(hash, auth[7..]))
        return Results.Unauthorized();

    try
    {
        return await next(context);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (IOException)
    {
        return Results.Conflict(new { error = "Операция с хранилищем не выполнена." });
    }
});

api.MapGet("/quota", async (CancellationToken ct) =>
{
    await changes.WaitAsync(ct);
    try { return Results.Ok(GetStorage()); }
    finally { changes.Release(); }
});

api.MapGet("", (string? path) =>
{
    var directory = Resolve(path ?? "", allowRoot: true);
    if (!Directory.Exists(directory))
        return Results.NotFound();
    var entries = new DirectoryInfo(directory).EnumerateFileSystemInfos()
        .Where(item => (item.Attributes & FileAttributes.ReparsePoint) == 0)
        .Select(item => Describe(item.FullName)).ToArray();
    return Results.Ok(entries);
});

api.MapGet("/thumbnail", async (string path, CancellationToken ct) =>
{
    var file = Resolve(path);
    if (!File.Exists(file))
        return Results.NotFound();
    var extension = Path.GetExtension(file).ToLowerInvariant();
    if (extension is not (".jpg" or ".jpeg" or ".png" or ".mp4"))
        return Results.NoContent();

    await thumbnailSlot.WaitAsync(ct);
    try
    {
        if (extension == ".mp4")
        {
            var frame = await VideoThumbnail.ReadAsync(file, ct);
            return frame is null ? Results.NoContent() : Results.Bytes(frame, "image/png");
        }

        await using var source = File.OpenRead(file);
        if (source.Length > 32 * 1024 * 1024)
            return Results.NoContent();

        using var codec = SKCodec.Create(source);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png) ||
            codec.Info.Width <= 0 || codec.Info.Height <= 0 ||
            (long)codec.Info.Width * codec.Info.Height > 24_000_000)
            return Results.NoContent();

        ct.ThrowIfCancellationRequested();
        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null)
            return Results.NoContent();

        var scale = Math.Min(1, 128.0 / Math.Max(bitmap.Width, bitmap.Height));
        var size = new SKImageInfo(Math.Max(1, (int)Math.Round(bitmap.Width * scale)),
            Math.Max(1, (int)Math.Round(bitmap.Height * scale)));
        using var surface = SKSurface.Create(size);
        surface.Canvas.Clear(SKColors.Transparent);
        using var image = SKImage.FromBitmap(bitmap);
        surface.Canvas.DrawImage(image, new SKRect(0, 0, size.Width, size.Height),
            new SKSamplingOptions(SKFilterMode.Linear));
        using var snapshot = surface.Snapshot();
        using var output = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return output is null ? Results.NoContent() : Results.Bytes(output.ToArray(), "image/png");
    }
    finally
    {
        thumbnailSlot.Release();
    }
});

api.MapGet("/content", (string path) =>
{
    var file = Resolve(path);
    return File.Exists(file)
        ? Results.File(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete),
            "application/octet-stream", Path.GetFileName(file), enableRangeProcessing: true)
        : Results.NotFound();
});

api.MapPut("/content", async (string path, HttpRequest request, CancellationToken ct) =>
{
    await changes.WaitAsync(ct);
    var temporary = Path.Combine(data, Guid.NewGuid().ToString("N") + ".upload");
    try
    {
        var file = Resolve(path);
        if (!Directory.Exists(Path.GetDirectoryName(file)))
            return Results.NotFound();
        if (Path.Exists(file))
            return Results.Conflict();

        var storage = GetStorage();
        var quotaRemaining = storage.QuotaBytes is long limit
            ? Math.Max(0, limit - storage.UsedBytes)
            : long.MaxValue;
        var budget = Math.Min(quotaRemaining, storage.AvailableBytes);
        var message = quotaRemaining <= storage.AvailableBytes
            ? "Превышена квота облака."
            : "На диске сервера недостаточно места.";
        IResult InsufficientSpace() => Results.Json(new { error = message }, statusCode: 507);
        if (request.ContentLength > budget)
            return InsufficientSpace();

        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 65536, useAsync: true))
        {
            var buffer = new byte[65536];
            int read;
            while ((read = await request.Body.ReadAsync(buffer, ct)) != 0)
            {
                if (read > budget)
                    return InsufficientSpace();
                await stream.WriteAsync(buffer.AsMemory(0, read), ct);
                budget -= read;
            }
        }
        ct.ThrowIfCancellationRequested();
        File.Move(temporary, file);
        return Results.Ok(Describe(file));
    }
    finally
    {
        try { File.Delete(temporary); }
        finally { changes.Release(); }
    }
});

api.MapPost("/folder", async (string path, CancellationToken ct) =>
{
    await changes.WaitAsync(ct);
    try
    {
        var folder = Resolve(path);
        if (!Directory.Exists(Path.GetDirectoryName(folder)))
            return Results.NotFound();
        if (Path.Exists(folder))
            return Results.Conflict();
        Directory.CreateDirectory(folder);
        return Results.Ok(Describe(folder));
    }
    finally
    {
        changes.Release();
    }
});

api.MapPost("/move", async (MoveRequest request, CancellationToken ct) =>
{
    await changes.WaitAsync(ct);
    try
    {
        var from = Resolve(request.From);
        var to = Resolve(request.To);
        if (!Path.Exists(from) || !Directory.Exists(Path.GetDirectoryName(to)))
            return Results.NotFound();
        if (Path.Exists(to))
            return Results.Conflict();
        if (Directory.Exists(from))
        {
            var relative = Path.GetRelativePath(from, to);
            if (!Path.IsPathRooted(relative) && relative != ".." &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "Папку нельзя переместить внутрь себя." });
            Directory.Move(from, to);
        }
        else
            File.Move(from, to);
        return Results.Ok(Describe(to));
    }
    finally
    {
        changes.Release();
    }
});

api.MapDelete("", async (string path, CancellationToken ct) =>
{
    await changes.WaitAsync(ct);
    try
    {
        var item = Resolve(path);
        if (File.Exists(item))
            File.Delete(item);
        else if (Directory.Exists(item))
            Directory.Delete(item);
        else
            return Results.NotFound();
        return Results.NoContent();
    }
    finally
    {
        changes.Release();
    }
});

app.Run();

StoredCredentials? ReadCredentials()
{
    if (!File.Exists(credentialsPath))
        return null;
    using var json = JsonDocument.Parse(File.ReadAllText(credentialsPath));
    return json.RootElement.ValueKind == JsonValueKind.String
        ? new StoredCredentials(json.RootElement.GetString()!, null)
        : json.RootElement.Deserialize<StoredCredentials>();
}

bool CanPair(string? code) => tokenHash is null && DateTimeOffset.UtcNow <= pairingExpires &&
    code is not null && Matches(Hash(pairingCode), code);

StorageUsage GetStorage()
{
    var comparer = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    var drive = DriveInfo.GetDrives()
        .Where(item => Path.TrimEndingDirectorySeparator(files).Equals(Path.TrimEndingDirectorySeparator(item.Name), comparer)
            || files.StartsWith(Path.EndsInDirectorySeparator(item.Name) ? item.Name : item.Name + Path.DirectorySeparatorChar, comparer))
        .OrderByDescending(item => item.Name.Length)
        .First();
    var used = new DirectoryInfo(files).EnumerateFiles("*", new EnumerationOptions
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false
    }).Sum(file => file.Length);
    return new StorageUsage(drive.TotalSize, drive.AvailableFreeSpace, used, quotaBytes);
}

string Resolve(string? path, bool allowRoot = false)
{
    if (path is "" && allowRoot)
        return files;
    if (string.IsNullOrEmpty(path))
        throw new ArgumentException("Укажите путь.");
    var current = files;
    foreach (var part in path.Split('/'))
    {
        if (part is "" or "." or ".." || part.IndexOfAny(['\\', ':', '\0']) >= 0 ||
            part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.TrimEnd(' ', '.') != part)
            throw new ArgumentException("Недопустимый путь.");
        current = Path.Combine(current, part);
        if (new FileInfo(current).LinkTarget != null ||
            (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0))
            throw new ArgumentException("Ссылки в хранилище не поддерживаются.");
    }
    return current;
}

object Describe(string path)
{
    FileSystemInfo item = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
    return new
    {
        item.Name,
        Path = Path.GetRelativePath(files, path).Replace(Path.DirectorySeparatorChar, '/'),
        IsFolder = item is DirectoryInfo,
        SizeBytes = item is FileInfo file ? file.Length : 0,
        CreatedAt = item.CreationTimeUtc,
        ModifiedAt = item.LastWriteTimeUtc
    };
}

static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
static bool Matches(string hash, string value) =>
    CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Convert.FromHexString(Hash(value)));

record PairRequest(string? Code, long? QuotaBytes);
record StoredCredentials(string TokenHash, long? QuotaBytes);
record StorageUsage(long DiskBytes, long AvailableBytes, long UsedBytes, long? QuotaBytes);
record MoveRequest(string From, string To);

