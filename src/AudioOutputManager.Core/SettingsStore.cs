using System.Text.Json;

namespace AudioOutputManager.Core;

public sealed record SettingsLoad(AppSettings Settings, string? Warning);

public sealed class SettingsStore(string directory)
{
    private readonly string path = Path.Combine(directory, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public SettingsLoad Load()
    {
        if (!File.Exists(path)) return new(new(), null);
        try { return new(Read(path), null); }
        catch (NotSupportedException) { throw; }
        catch (Exception error) when (error is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            // Preserve corrupt originals; never silently replace newer versions.
            string corrupt = path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
            try { File.Copy(path, corrupt, false); } catch (Exception ex) when(ex is IOException or UnauthorizedAccessException) { }
            try { return new(Read(path + ".bak"), "設定損壞，已讀取最近有效備份。"); }
            catch (Exception backupError) when (backupError is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
            { return new(new(), "設定與備份無法讀取，採安全預設；損壞原件已盡可能保留。"); }
        }
    }

    private static AppSettings Read(string file)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), Json) ?? throw new InvalidDataException("設定為空。");
        if (settings.Version > AppSettings.CurrentVersion) throw new NotSupportedException("設定由較新版程式建立，禁止降級覆寫。");
        settings.Validate();
        return settings;
    }

    public void Save(AppSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(directory);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, settings, Json); stream.Flush(true); }
            if (File.Exists(path))
            {
                // Do not turn a damaged current file into the "last good" backup.
                try { Read(path); File.Replace(temporary, path, path + ".bak", true); }
                catch (Exception ex) when (ex is JsonException or InvalidDataException)
                { File.Move(temporary, path, true); }
            }
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
