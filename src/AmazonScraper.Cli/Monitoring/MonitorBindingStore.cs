using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AmazonScraper.Cli.Storage;
using AmazonScraper.Core.Monitoring;

namespace AmazonScraper.Cli.Monitoring;

public sealed class MonitorBindingStore
{
    public const int CurrentSchema = 1;
    private const int LockAttempts = 100;
    private const int LockDelayMilliseconds = 25;

    private readonly string _path;
    private readonly string _lockPath;

    public MonitorBindingStore(
        string? path = null,
        string? lockPath = null)
    {
        _path = Path.GetFullPath(
            path ?? UserStatePaths.MonitorBindingsPath());
        _lockPath = Path.GetFullPath(lockPath ?? $"{_path}.lock");
    }

    public string FilePath => _path;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public MonitorBindingDocument Read()
    {
        using var gate = AcquireLock();
        return ReadUnlocked();
    }

    public MonitorBinding? Find(string id)
    {
        using var gate = AcquireLock();
        var document = ReadUnlocked();
        return document.Bindings.FirstOrDefault(binding =>
            string.Equals(binding.Id, id, StringComparison.Ordinal));
    }

    public MonitorBindingDocument Upsert(MonitorBinding binding)
    {
        using var gate = AcquireLock();
        var document = ReadUnlocked();
        var normalized = Normalize(binding);
        var index = document.Bindings.FindIndex(item =>
            string.Equals(item.Id, normalized.Id, StringComparison.Ordinal));
        if (index >= 0)
        {
            normalized.CreatedAt = document.Bindings[index].CreatedAt;
            document.Bindings[index] = normalized;
        }
        else
        {
            document.Bindings.Add(normalized);
        }
        normalized.UpdatedAt = DateTimeOffset.UtcNow;
        document.Revision++;
        document.UpdatedAt = DateTimeOffset.UtcNow;
        Write(document);
        return document;
    }

    public MonitorBindingDocument Remove(string id)
    {
        using var gate = AcquireLock();
        var document = ReadUnlocked();
        var removed = document.Bindings.RemoveAll(binding =>
            string.Equals(binding.Id, id, StringComparison.Ordinal)) > 0;
        if (!removed)
        {
            return document;
        }
        document.Revision++;
        document.UpdatedAt = DateTimeOffset.UtcNow;
        Write(document);
        return document;
    }

    private MonitorBindingDocument ReadUnlocked()
    {
        if (!File.Exists(_path))
        {
            return new MonitorBindingDocument();
        }

        try
        {
            var document = JsonSerializer.Deserialize<MonitorBindingDocument>(
                File.ReadAllText(_path),
                JsonOptions) ?? new MonitorBindingDocument();
            if (document.Schema > CurrentSchema)
            {
                throw new MonitorException(
                    $"Monitor binding schema {document.Schema} is newer than " +
                    $"the supported schema {CurrentSchema}.");
            }
            document.Schema = CurrentSchema;
            document.Revision = Math.Max(0, document.Revision);
            document.Bindings ??= [];
            document.Bindings = document.Bindings
                .Where(binding => binding is not null)
                .Select(binding => Normalize(binding))
                .GroupBy(binding => binding.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(binding => binding.Id, StringComparer.Ordinal)
                .ToList();
            return document;
        }
        catch (MonitorException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or JsonException
                or NotSupportedException)
        {
            throw new MonitorException(
                $"Unable to read monitor bindings '{_path}': {ex.Message}");
        }
    }

    private static MonitorBinding Normalize(MonitorBinding source)
    {
        var kind = (source.Kind ?? string.Empty).Trim().ToLowerInvariant();
        if (kind is not (MonitorBindingKind.Product or MonitorBindingKind.Group))
        {
            throw new MonitorException(
                $"Invalid monitor binding kind: '{source.Kind}'.");
        }

        var productId = (source.ProductId ?? string.Empty).Trim();
        var groupId = (source.GroupId ?? string.Empty).Trim();
        if (kind == MonitorBindingKind.Product && productId.Length == 0)
        {
            throw new MonitorException(
                "A product monitor binding requires product_id.");
        }
        if (kind == MonitorBindingKind.Group && groupId.Length == 0)
        {
            throw new MonitorException(
                "A group monitor binding requires group_id.");
        }

        var id = (source.Id ?? string.Empty).Trim();
        if (id.Length == 0)
        {
            id = kind == MonitorBindingKind.Product
                ? MonitorBinding.ProductBindingId(productId)
                : MonitorBinding.GroupBindingId(groupId);
        }
        if (source.MaxPrice is <= 0)
        {
            throw new MonitorException("max_price must be greater than zero.");
        }
        if (source.DropPercent is <= 0 or > 100)
        {
            throw new MonitorException("drop_percent must be between 0 and 100.");
        }
        if (source.RisePercent is <= 0 or > 100)
        {
            throw new MonitorException("rise_percent must be between 0 and 100.");
        }
        if (source.TotalMax is <= 0)
        {
            throw new MonitorException("total_max must be greater than zero.");
        }
        if (source.CooldownSeconds is < 0)
        {
            throw new MonitorException("cooldown_seconds cannot be negative.");
        }
        if (source.BaselinePrice is <= 0)
        {
            throw new MonitorException("baseline_price must be greater than zero.");
        }

        var fx = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var (currency, rate) in source.Fx ?? [])
        {
            var key = (currency ?? string.Empty).Trim().ToUpperInvariant();
            if (key.Length == 0 || rate <= 0)
            {
                throw new MonitorException(
                    "FX rates require a currency and a positive multiplier.");
            }
            fx[key] = rate;
        }

        var itemRules = new Dictionary<string, MonitorBindingItemRule>(
            StringComparer.Ordinal);
        foreach (var (productKey, rule) in source.ItemRules ?? [])
        {
            var key = (productKey ?? string.Empty).Trim();
            if (key.Length == 0 || rule is null)
            {
                continue;
            }
            if (rule.MaxPrice is <= 0)
            {
                throw new MonitorException(
                    $"max_price for '{key}' must be greater than zero.");
            }
            if (rule.DropPercent is <= 0 or > 100)
            {
                throw new MonitorException(
                    $"drop_percent for '{key}' must be between 0 and 100.");
            }
            if (rule.RisePercent is <= 0 or > 100)
            {
                throw new MonitorException(
                    $"rise_percent for '{key}' must be between 0 and 100.");
            }
            if (rule.BaselinePrice is <= 0)
            {
                throw new MonitorException(
                    $"baseline_price for '{key}' must be greater than zero.");
            }
            if (rule.CooldownSeconds is < 0)
            {
                throw new MonitorException(
                    $"cooldown_seconds for '{key}' cannot be negative.");
            }
            itemRules[key] = rule;
        }

        return new MonitorBinding
        {
            Id = id,
            Kind = kind,
            ProductId = kind == MonitorBindingKind.Product ? productId : null,
            GroupId = kind == MonitorBindingKind.Group ? groupId : null,
            Name = string.IsNullOrWhiteSpace(source.Name)
                ? null
                : source.Name.Trim(),
            MaxPrice = source.MaxPrice,
            DropPercent = source.DropPercent,
            RisePercent = source.RisePercent,
            Baseline = string.IsNullOrWhiteSpace(source.Baseline)
                ? null
                : source.Baseline.Trim(),
            BaselinePrice = source.BaselinePrice,
            CooldownSeconds = source.CooldownSeconds,
            TotalMax = source.TotalMax,
            AllowIncomplete = source.AllowIncomplete,
            Currency = string.IsNullOrWhiteSpace(source.Currency)
                ? null
                : source.Currency.Trim().ToUpperInvariant(),
            Fx = fx,
            ItemRules = itemRules,
            Runtime = NormalizeRuntime(source.Runtime),
            CreatedAt = source.CreatedAt == default
                ? DateTimeOffset.UtcNow
                : source.CreatedAt,
            UpdatedAt = source.UpdatedAt == default
                ? DateTimeOffset.UtcNow
                : source.UpdatedAt,
        };
    }

    private static MonitorRuntimeSettings? NormalizeRuntime(
        MonitorRuntimeSettings? source)
    {
        if (source is null)
        {
            return null;
        }

        if (source.TimerSeconds is { } timer && timer is < 10 or > 2_592_000)
        {
            throw new MonitorException(
                "runtime.timer_seconds must be between 10 and 2592000.");
        }
        if (source.RequestDelaySeconds is { } delay && delay is < 6 or > 3600)
        {
            throw new MonitorException(
                "runtime.request_delay_seconds must be between 6 and 3600.");
        }
        if (source.JitterSeconds is < 0 or > 3600)
        {
            throw new MonitorException(
                "runtime.jitter_seconds must be between 0 and 3600.");
        }
        if (source.NotifyCooldownSeconds is { } cooldown
            && cooldown is < 60 or > 604_800)
        {
            throw new MonitorException(
                "runtime.notify_cooldown_seconds must be between 60 and 604800.");
        }

        var format = (source.SaveFormat ?? string.Empty).Trim().ToLowerInvariant();
        if (format.Length > 0 && format is not ("json" or "jsonl" or "csv"))
        {
            throw new MonitorException(
                "runtime.save_format must be json, jsonl, or csv.");
        }
        var layout = (source.Layout ?? string.Empty).Trim().ToLowerInvariant();
        if (layout.Length > 0
            && layout is not ("auto" or "combined" or "per-group" or "single"))
        {
            throw new MonitorException(
                "runtime.layout must be auto, combined, per-group, or single.");
        }
        var effectiveFormat = format.Length == 0 ? "json" : format;
        var effectiveLayout = layout.Length == 0 ? "auto" : layout;
        if (string.Equals(effectiveLayout, "single", StringComparison.Ordinal)
            && string.Equals(effectiveFormat, "csv", StringComparison.Ordinal))
        {
            throw new MonitorException(
                "runtime.layout single is only supported with json/jsonl.");
        }

        var saveFile = (source.SaveFile ?? string.Empty).Trim();
        if (saveFile.Length > 0
            && (saveFile is "." or ".."
                || saveFile.Contains('/')
                || saveFile.Contains('\\')
                || saveFile.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new MonitorException(
                "runtime.save_file must be a simple file name.");
        }

        var notifyOn = (source.NotifyOn ?? [])
            .Select(value => (value ?? string.Empty).Trim().ToLowerInvariant())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new MonitorRuntimeSettings
        {
            TimerSeconds = source.TimerSeconds,
            RequestDelaySeconds = source.RequestDelaySeconds,
            JitterSeconds = source.JitterSeconds,
            SavePath = string.IsNullOrWhiteSpace(source.SavePath)
                ? null
                : source.SavePath.Trim(),
            SaveFile = saveFile.Length == 0 ? null : saveFile,
            SaveFormat = format.Length == 0 ? null : format,
            Layout = layout.Length == 0 ? null : layout,
            Notify = source.Notify ?? false,
            NotifyOn = notifyOn.Count == 0 ? ["alerts"] : notifyOn,
            NotifyCooldownSeconds = source.NotifyCooldownSeconds,
            NoAutoUpdate = source.NoAutoUpdate ?? false,
        };
    }

    private void Write(MonitorBindingDocument document)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            TrySetUnixMode(
                directory,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }

        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(document, JsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            TrySetUnixMode(
                temporary,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, _path, overwrite: true);
            TrySetUnixMode(
                _path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception)
            {
                // Best effort.
            }
            throw new MonitorException(
                $"Unable to write monitor bindings '{_path}': {ex.Message}");
        }
    }

    private FileStream AcquireLock()
    {
        var directory = Path.GetDirectoryName(_lockPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            TrySetUnixMode(
                directory,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }

        for (var attempt = 0; attempt < LockAttempts; attempt++)
        {
            try
            {
                var stream = new FileStream(
                    _lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);
                TrySetUnixMode(
                    _lockPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
                return stream;
            }
            catch (IOException) when (attempt + 1 < LockAttempts)
            {
                Thread.Sleep(LockDelayMilliseconds);
            }
            catch (IOException ex)
            {
                throw new MonitorException(
                    $"Monitor bindings are busy: {_lockPath}");
            }
        }

        throw new MonitorException(
            $"Monitor bindings are busy: {_lockPath}");
    }

    private static void TrySetUnixMode(string path, UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, mode);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
