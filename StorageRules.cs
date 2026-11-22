using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class StorageRules
{
    internal static StoredCredentials? ReadCredentials(string credentialsPath)
    {
        string contents;
        try { contents = File.ReadAllText(credentialsPath); }
        catch (FileNotFoundException) { return null; }

        using var json = JsonDocument.Parse(contents);
        var stored = json.RootElement.ValueKind == JsonValueKind.String
            ? new StoredCredentials(json.RootElement.GetString()!, null)
            : json.RootElement.Deserialize<StoredCredentials>();
        if (stored?.TokenHash is not { Length: 64 } hash || !hash.All(Uri.IsHexDigit))
            throw new InvalidDataException("Повреждены данные подключения облака.");
        return stored;
    }

    internal static bool CanPair(string? tokenHash, DateTimeOffset now, DateTimeOffset expires,
        string codeHash, string? submittedCode) =>
        tokenHash is null && now <= expires && submittedCode is not null && Matches(codeHash, submittedCode);

    internal static bool IsPairingQuotaValid(long? quota, StorageUsage storage) =>
        quota is not long limit ||
        (limit > 0 && limit >= storage.UsedBytes && limit <= storage.UsedBytes + storage.AvailableBytes);

    internal static string Resolve(string files, string? path, bool allowRoot = false)
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

    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static bool Matches(string hash, string value) =>
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Convert.FromHexString(Hash(value)));
}
