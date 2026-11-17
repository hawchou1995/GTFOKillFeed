using System;
using System.IO;
using HawChow.GTFOKillFeed;

internal static class NameFileTests
{
    internal static void Run(Action<bool, string> check)
    {
        string scratch = Environment.GetEnvironmentVariable("PI_SCRATCH_DIR");
        if (string.IsNullOrWhiteSpace(scratch)) throw new InvalidOperationException("PI_SCRATCH_DIR is required for name-file tests.");
        BepInEx.Paths.ConfigPath = Path.Combine(scratch, "killfeed-name-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(BepInEx.Paths.ConfigPath);
        EnemyNames.Initialize();
        check(File.Exists(EnemyNames.FilePath), "first load creates full names file in isolated scratch");
        int initialRevision = EnemyNames.Revision;
        check(initialRevision > 0, "successful initial load publishes a name-table revision");
        check(EnemyNames.Resolve(13, "Shooter") == "小白", "actual name loader reads bundled default table");
        check(EnemyNames.Resolve(11, "Striker") == "菜花", "actual name loader retains upstream vocabulary");
        check(EnemyNames.Resolve(9000, "CustomBoss") == "CustomBoss", "actual name loader handles unknown mod IDs");
        File.WriteAllText(EnemyNames.FilePath, "{\"Rules\":[{\"DataBlockId\":13,\"RawName\":\"CustomShooter\",\"DisplayName\":\"测试射手\"}]}");
        File.SetLastWriteTimeUtc(EnemyNames.FilePath, DateTime.UtcNow.AddSeconds(2));
        EnemyNames.Tick(3f);
        check(EnemyNames.Resolve(13, "CustomShooter") == "测试射手", "saving custom name reloads without engine");
        check(EnemyNames.Revision > initialRevision, "valid custom-name reload advances revision for cached HUD labels");
        File.WriteAllText(EnemyNames.FilePath, "{broken json");
        File.SetLastWriteTimeUtc(EnemyNames.FilePath, DateTime.UtcNow.AddSeconds(4));
        EnemyNames.Tick(6f);
        check(EnemyNames.Resolve(13, "CustomShooter") == "测试射手", "malformed file keeps previous good custom names");
        File.WriteAllText(EnemyNames.FilePath, "{\"UseHikariaDefaults\":false,\"Rules\":[]}");
        File.SetLastWriteTimeUtc(EnemyNames.FilePath, DateTime.UtcNow.AddSeconds(6));
        EnemyNames.Tick(9f);
        check(EnemyNames.Resolve(13, "Shooter") == "Shooter", "file switch disables Hikaria defaults");
        string before = File.ReadAllText(EnemyNames.FilePath);
        EnemyNames.Initialize();
        check(File.ReadAllText(EnemyNames.FilePath) == before, "initialization never overwrites existing name config");
    }
}

// Test-only host boundary. Production compiles against real BepInEx and Plugin.
namespace BepInEx
{
    internal static class Paths { internal static string ConfigPath; }
}
namespace HawChow.GTFOKillFeed
{
    internal sealed class Plugin
    {
        internal static readonly Plugin Instance = new();
        internal readonly TestLog Log = new();
    }
    internal sealed class TestLog
    {
        internal void LogInfo(object message) { }
        internal void LogWarning(object message) => Console.WriteLine("EXPECTED_TEST_LOG: " + message);
    }
}
