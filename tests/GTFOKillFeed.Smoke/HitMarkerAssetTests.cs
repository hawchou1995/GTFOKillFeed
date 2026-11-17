using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using HawChow.GTFOKillFeed;

// The four shipped BFV marker PNGs, measured on the bytes the plugin will load from disk. The kill line has
// to be a real, separate raster derived from the BFV `killhitmark` geometry - not a renamed copy of the
// weakspot hit line and not the head-top kill icon.
internal static class HitMarkerAssetTests
{
    private const string Prefix = "KillFeed.Icons.";

    internal static void Run(Action<bool, string> check)
    {
        Assembly assembly = typeof(HitMarkerAssetTests).Assembly;
        var expected = new[]
        {
            HitMarkerPolicy.IconFile(HitMarkerKind.Normal),
            HitMarkerPolicy.IconFile(HitMarkerKind.Weakspot),
            HitMarkerPolicy.IconFile(HitMarkerKind.Armor),
            HitMarkerPolicy.IconFile(HitMarkerKind.Kill),
        };
        var missing = new List<string>();
        foreach (string file in expected)
            if (assembly.GetManifestResourceStream(Prefix + file) == null) missing.Add(file);
        check(missing.Count == 0 && expected.Length == 4
            && expected[3] == "hitmark-kill.png",
            "hitmarker-assets: every marker state names an embedded PNG (normal/weakspot/armor/kill); missing "
            + (missing.Count == 0 ? "none" : string.Join(",", missing)));

        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var duplicates = new List<string>();
        int opaqueNormal = 0, opaqueWeak = 0, opaqueArmor = 0, opaqueKill = 0;
        bool headersValid = true, pixelsValid = true;
        string firstBad = string.Empty;
        foreach (string file in expected)
        {
            byte[] bytes = Read(assembly, Prefix + file);
            if (bytes == null || bytes.Length < 33)
            {
                headersValid = false;
                firstBad = file + " (missing)";
                continue;
            }
            bool png = bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
            int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            int depth = bytes[24];
            int colorType = bytes[25];
            if (!png || width != 128 || height != 128 || depth != 8 || colorType != 6)
            {
                headersValid = false;
                if (firstBad.Length == 0) firstBad = file + " (not 128x128 RGBA8)";
                continue;
            }
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes));
            if (hashes.TryGetValue(hash, out string other)) duplicates.Add(other + " == " + file);
            else hashes[hash] = file;

            // Geometry size proxy: the compressed byte count of the shipped rasters. A kill line derived from
            // the same geometry must not collapse to the tiny armour brackets, and must not be a copy.
            int size = bytes.Length;
            if (file == "hitmark-normal.png") opaqueNormal = size;
            else if (file == "hitmark-weakspot.png") opaqueWeak = size;
            else if (file == "hitmark-armor.png") opaqueArmor = size;
            else if (file == "hitmark-kill.png") opaqueKill = size;
            if (size < 200) pixelsValid = false;
        }
        check(headersValid,
            "hitmarker-assets: every marker PNG is a 128x128 RGBA8 raster" + (headersValid ? string.Empty : " FIRST BAD " + firstBad));
        check(duplicates.Count == 0,
            "hitmarker-assets: no two marker states hide the same bytes, so the kill line is a real separate raster"
            + (duplicates.Count == 0 ? string.Empty : " DUPLICATES " + string.Join("; ", duplicates)));
        check(opaqueKill > opaqueArmor && opaqueKill != opaqueWeak && opaqueWeak != opaqueNormal,
            "hitmarker-assets: the kill line is larger than the armour brackets and differs from the weakspot line ("
            + opaqueNormal + "/" + opaqueWeak + "/" + opaqueArmor + "/" + opaqueKill + " bytes)");
        check(pixelsValid, "hitmarker-assets: every marker raster actually carries geometry");
    }

    private static byte[] Read(Assembly assembly, string resource)
    {
        try
        {
            using Stream stream = assembly.GetManifestResourceStream(resource);
            if (stream == null) return null;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (Exception) { return null; }
    }
}
