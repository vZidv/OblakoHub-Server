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
var pairingCode = RandomNumberGenerator.GetHexString(32);
var pairingExpires = DateTimeOffset.UtcNow.AddMinutes(15);
var tokenHash = File.Exists(credentialsPath)
    ? JsonSerializer.Deserialize<string>(File.ReadAllText(credentialsPath))
    : null;

if (tokenHash is null)
    Console.WriteLine($"Код подключения (15 минут): {pairingCode}");

app.MapGet("/api/v1/health", () => Results.Ok(new { apiVersion = 1 }));
app.MapPost("/api/v1/pair", async (PairRequest request) =>
{
    await changes.WaitAsync();
    try
    {
        if (tokenHash is not null || DateTimeOffset.UtcNow > pairingExpires ||
            request.Code is null || !Matches(Hash(pairingCode), request.Code))
            return Results.Unauthorized();

        var token = RandomNumberGenerator.GetHexString(64);
        var hash = Hash(token);
        await File.WriteAllTextAsync(credentialsPath + ".tmp", JsonSerializer.Serialize(hash));
        File.Move(credentialsPath + ".tmp", credentialsPath, overwrite: true);
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
    var file = Resolve(path);
    if (!Directory.Exists(Path.GetDirectoryName(file)))
        return Results.NotFound();
    var temporary = Path.Combine(data, Guid.NewGuid().ToString("N") + ".upload");
    try
    {
        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 65536, useAsync: true))
            await request.Body.CopyToAsync(stream, ct);

        await changes.WaitAsync(ct);
        try
        {
            File.Move(temporary, Resolve(path));
            return Results.Ok(Describe(file));
        }
        finally
        {
            changes.Release();
        }
    }
    finally
    {
        File.Delete(temporary);
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

record PairRequest(string? Code);
record MoveRequest(string From, string To);

