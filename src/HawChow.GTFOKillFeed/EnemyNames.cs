using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BepInEx;

namespace HawChow.GTFOKillFeed;

internal static class EnemyNames
{
    private static List<EnemyNameRule> _rules = new();
    private static Dictionary<uint, string> _defaults = new();
    private static NameFile _bundled;
    private static DateTime _stamp;
    private static float _checkAt;
    internal static int Revision { get; private set; }

    private static bool _useDefaults = true;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    internal static string FilePath => Path.Combine(Paths.ConfigPath, "HawChow.KillFeed.EnemyNames.json");
    internal static void Initialize()
    {
        try
        {
            using var stream = typeof(Plugin).Assembly.GetManifestResourceStream("KillFeed.EnemyNames.defaults.json");
            if (stream == null) throw new InvalidOperationException("Bundled Hikaria name table missing.");
            _bundled = JsonSerializer.Deserialize<NameFile>(stream, JsonOptions);
            _defaults = BuildDefaults(_bundled.Names);
            if (!File.Exists(FilePath)) File.WriteAllText(FilePath, JsonSerializer.Serialize(_bundled, JsonOptions));
            Reload();
        }
        catch (Exception error) { Plugin.Instance.Log.LogWarning("[NAMES] initialization failed: " + error.Message); }
    }
    internal static void Tick(float now)
    {
        if (now < _checkAt) return;
        _checkAt = now + 2f;
        try { if (File.GetLastWriteTimeUtc(FilePath) != _stamp) Reload(); }
        catch (Exception error) { Plugin.Instance.Log.LogWarning("[NAMES] file check failed: " + error.Message); }
    }
    internal static string Resolve(uint id, string raw) => EnemyNamePolicy.Resolve(id, raw, _rules, _useDefaults ? _defaults : null);
    private static void Reload()
    {
        try
        {
            _stamp = File.GetLastWriteTimeUtc(FilePath);
            var file = new FileInfo(FilePath);
            if (!file.Exists || file.Length > 256 * 1024) throw new FormatException("Name file missing or exceeds 256 KiB.");
            var config = JsonSerializer.Deserialize<NameFile>(File.ReadAllText(FilePath), JsonOptions);
            if (config?.Rules == null || config.Rules.Count > 1024) throw new FormatException("Rules must be an array with at most 1024 entries.");
            foreach (var rule in config.Rules)
                if (!EnemyNamePolicy.Valid(rule)) throw new FormatException("Rules need RawName and a single-line DisplayName up to 96 characters.");
            var defaults = BuildDefaults(config.Names ?? _bundled?.Names ?? new List<NameGroup>());
            _rules = config.Rules; _defaults = defaults; _useDefaults = config.UseHikariaDefaults; Revision++;
            Plugin.Instance.Log.LogInfo($"[NAMES] defaults={_defaults.Count};enabled={_useDefaults};overrides={_rules.Count};file={FilePath};custom-rundown IDs may override defaults by exact raw name.");
        }
        catch (Exception error) { Plugin.Instance.Log.LogWarning("[NAMES] keeping previous valid mappings: " + error.Message); }
    }
    private static Dictionary<uint, string> BuildDefaults(List<NameGroup> groups)
    {
        if (groups == null || groups.Count > 1024) throw new FormatException("Invalid default name groups.");
        var result = new Dictionary<uint, string>();
        foreach (var group in groups)
        {
            if (group.IDs == null || group.IDs.Count > 1024 || string.IsNullOrWhiteSpace(group.Name) || group.Name.Length > 96 || group.Name.Contains('\n') || group.Name.Contains('\r')) throw new FormatException("Invalid name group.");
            foreach (uint id in group.IDs) if (id != 0) result[id] = group.Name;
        }
        if (result.Count > 4096) throw new FormatException("Default map exceeds 4096 IDs.");
        return result;
    }
    private sealed class NameFile
    {
        public NameFile() { }
        public string Source { get; set; }
        public bool UseHikariaDefaults { get; set; } = true;
        public List<NameGroup> Names { get; set; }
        public List<EnemyNameRule> Rules { get; set; } = new();
    }
    private sealed class NameGroup
    {
        public NameGroup() { }
        public List<uint> IDs { get; set; }
        public string Name { get; set; }
    }
}
