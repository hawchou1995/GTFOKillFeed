using System;
using System.IO;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

// Unity adapter for the icon pipeline. It owns exactly two things Unity insists on: creating /
// destroying Texture2D on the main thread, and reading the plugin folder path. All scheduling,
// stamp comparison, backoff and old-texture handling live in IconPipeline so the same logic is
// exercised by the smoke tests.
internal sealed class IconTextureCache
{
    private readonly IconPipeline _pipeline;
    private readonly DiagnosticBudget _warnings = new();

    internal IconTextureCache()
        : this(new FileIconIo(), new UnityIconSink(), ThreadPoolIconJobRunner.Instance) { }

    internal IconTextureCache(IIconIo io, IIconSink sink, IIconJobRunner runner)
    {
        _pipeline = new IconPipeline(io, sink, runner, message =>
        {
            if (_warnings.TryUse(8)) Plugin.Instance?.Log.LogWarning("[HUD] " + message);
        });
    }

    internal IconPipeline Pipeline => _pipeline;

    // Update() only: may start a background probe, never touches the disk on this thread.
    internal void Tick(string key, string path, float now) => _pipeline.Tick(key, path, now);

    // OnGUI() only: pure dictionary lookup, no IO at all.
    internal Texture2D Texture(string key) => _pipeline.Texture(key) as Texture2D;

    internal static string IconRoot()
    {
        try
        {
            string folder = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
            return Path.Combine(string.IsNullOrEmpty(folder) ? string.Empty : folder, "Assets", "icons");
        }
        catch { return "Assets/icons"; }
    }

    internal static bool TryStamp(string path, out DateTime stamp, out long length)
    {
        stamp = DateTime.MinValue;
        length = -1;
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return false;
            stamp = info.LastWriteTimeUtc;
            length = info.Length;
            return true;
        }
        catch { return false; }
    }
}

// Worker-thread file access with a hard size cap and shared-read so a player editing the PNG in an
// image editor cannot dead-lock the probe.
internal sealed class FileIconIo : IIconIo
{
    private readonly long _fallbackMaximum;

    internal FileIconIo() : this(IconPipeline.MaxIconBytes) { }

    internal FileIconIo(long fallbackMaximum)
    {
        _fallbackMaximum = fallbackMaximum > 0 ? fallbackMaximum : IconPipeline.MaxIconBytes;
    }

    public bool TryProbe(string path, out DateTime writeUtc, out long length, out string error)
    {
        writeUtc = DateTime.MinValue;
        length = -1;
        error = null;
        if (string.IsNullOrWhiteSpace(path)) { error = "empty path"; return false; }
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) { error = "missing"; return false; }
            writeUtc = info.LastWriteTimeUtc;
            length = info.Length;
            if (length <= 0) { error = "empty file"; return false; }
            if (length > _fallbackMaximum) { error = "file too large"; return false; }
            return true;
        }
        catch (Exception exception) { error = exception.GetType().Name; return false; }
    }

    public byte[] TryRead(string path, long maximumBytes, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path)) { error = "empty path"; return null; }
        long limit = maximumBytes > 0 ? maximumBytes : _fallbackMaximum;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!stream.CanRead) { error = "unreadable"; return null; }
            if (stream.Length <= 0) { error = "empty file"; return null; }
            if (stream.Length > limit) { error = "file too large"; return null; }
            var buffer = new byte[stream.Length];
            int read = 0;
            while (read < buffer.Length)
            {
                int chunk = stream.Read(buffer, read, buffer.Length - read);
                if (chunk <= 0) break;
                read += chunk;
            }
            if (read != buffer.Length) { error = "short read (half-written file?)"; return null; }
            return buffer;
        }
        catch (Exception exception) { error = exception.GetType().Name; return null; }
    }
}

// Main-thread texture owner.
internal sealed class UnityIconSink : IIconSink
{
    public object Create(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes))
        {
            UnityEngine.Object.Destroy(texture);
            return null;
        }
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        return texture;
    }

    public void Release(object texture)
    {
        if (texture is Texture2D typed && typed != null) UnityEngine.Object.Destroy(typed);
    }
}
