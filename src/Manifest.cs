using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NightRunnersMP.Launcher;

internal sealed class ManifestFile
{
    public string Hash;
    public long Size;
    // only seeded once, after that the file is the player's
    public bool Once;
    public string Path;
    // patches only: the file that has to be there first, so we fix someone else's mod and never install it
    public string Needs;
}

// file list of a release, from the github release. trust nothing until the sig checks out against the key in this exe
internal sealed class Manifest
{
    private static readonly byte[] PublicX = Convert.FromBase64String("bmYhJWytV0/u19a3Z294vrxLNmfKOOTid8CMeBnaInY=");
    private static readonly byte[] PublicY = Convert.FromBase64String("KP7XGC71XVzSQ4bd2X4HJ+T5kSKTB9lwcD/DkumlMlc=");

    public string Version;
    // when it was signed, so an old release can't be passed off as the newest one
    public long Issued;
    public string GameHash;
    public string LauncherHash;
    public long LauncherSize;
    public string Host;
    public int Port;
    // the zip on the github release with every file in it, at its game folder path. the same zip people install by hand
    public string PackName;
    public string PackHash;
    public long PackSize;
    // files that are also on the release on their own, by hash, so a mod only update doesn't pull the whole zip
    public readonly Dictionary<string, string> Loose = new Dictionary<string, string>();
    public readonly List<ManifestFile> Files = new List<ManifestFile>();
    // fixes on top of a mod the player installed themselves, like array's C1. own line type so older launchers skip them
    public readonly List<ManifestFile> Patches = new List<ManifestFile>();

    public static Manifest Parse(byte[] data)
    {
        var newline = Array.IndexOf(data, (byte)'\n');
        if (newline < 0)
            throw new InvalidDataException("empty manifest");
        var first = Encoding.ASCII.GetString(data, 0, newline).Trim();
        if (!first.StartsWith("sig "))
            throw new InvalidDataException("unsigned manifest");

        var body = new byte[data.Length - newline - 1];
        Array.Copy(data, newline + 1, body, 0, body.Length);
        if (!IsSigned(body, Convert.FromBase64String(first.Substring(4))))
            throw new InvalidDataException("bad manifest signature");

        // signed, so a bad line means my publish went wrong. still nothing gets used past what its field can be
        var manifest = new Manifest();
        foreach (var line in Encoding.UTF8.GetString(body).Split('\n'))
        {
            var parts = line.Split(new[] { ' ' }, 5);
            switch (parts[0])
            {
                case "version": manifest.Version = Field(parts, 1, VersionShape); break;
                case "issued": manifest.Issued = Number(parts, 1); break;
                case "game": manifest.GameHash = Field(parts, 1, HashShape); break;
                case "server":
                    manifest.Host = Field(parts, 1, HostShape);
                    manifest.Port = (int)Number(parts, 2, 65535);
                    break;
                case "launcher":
                    manifest.LauncherHash = Field(parts, 1, HashShape);
                    manifest.LauncherSize = Number(parts, 2);
                    break;
                case "pack":
                    manifest.PackHash = Field(parts, 1, HashShape);
                    manifest.PackSize = Number(parts, 2);
                    manifest.PackName = Field(parts, 3, AssetShape);
                    break;
                case "loose":
                    manifest.Loose[Field(parts, 1, HashShape)] = Field(parts, 2, AssetShape);
                    break;
                case "file":
                    manifest.Files.Add(new ManifestFile
                    {
                        Hash = Field(parts, 1, HashShape),
                        Size = Number(parts, 2),
                        Once = Field(parts, 3, ModeShape) == "once",
                        Path = SafePath(Field(parts, 4, null)),
                    });
                    break;
                case "patch":
                    manifest.Patches.Add(new ManifestFile
                    {
                        Hash = Field(parts, 1, HashShape),
                        Size = Number(parts, 2),
                        Needs = SafePath(Field(parts, 3, null)),
                        Path = SafePath(Field(parts, 4, null)),
                    });
                    break;
            }
        }
        // no host needed, the github release has no server line. players put the server in their cfg themselves
        if (manifest.Version == null || manifest.GameHash == null || manifest.LauncherHash == null)
            throw new InvalidDataException("manifest is missing lines");
        return manifest;
    }

    private static readonly Regex HashShape = new Regex("^[0-9a-f]{64}$");
    private static readonly Regex VersionShape = new Regex("^[0-9A-Za-z.+-]{1,32}$");
    private static readonly Regex HostShape = new Regex("^[0-9A-Za-z.-]{1,253}$");
    private static readonly Regex AssetShape = new Regex("^[0-9A-Za-z][0-9A-Za-z._-]{0,99}$");
    private static readonly Regex ModeShape = new Regex("^(always|once)$");
    private static readonly Regex DeviceName = new Regex(@"^(con|prn|aux|nul|com\d|lpt\d)(\..*)?$", RegexOptions.IgnoreCase);

    private static string Field(string[] parts, int index, Regex shape)
    {
        if (index >= parts.Length || shape != null && !shape.IsMatch(parts[index]))
            throw new InvalidDataException($"bad manifest line {string.Join(" ", parts)}");
        return parts[index];
    }

    private static long Number(string[] parts, int index, long max = 1L << 40)
    {
        if (index >= parts.Length || !long.TryParse(parts[index], out var value) || value < 0 || value > max)
            throw new InvalidDataException($"bad manifest line {string.Join(" ", parts)}");
        return value;
    }

    // a plain path that stays inside the game folder. no drive, no .., none of the names windows treats special
    // like CON or a name ending in a dot
    public static string SafePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 240 || System.IO.Path.IsPathRooted(path))
            throw new InvalidDataException($"bad path {path}");
        foreach (var part in path.Split('/'))
        {
            if (part.Trim('.').Length == 0 || part.EndsWith(".") || part.Trim() != part
                || part.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || DeviceName.IsMatch(part))
                throw new InvalidDataException($"bad path {path}");
        }
        return path;
    }

    private static bool IsSigned(byte[] body, byte[] signature)
    {
        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = PublicX, Y = PublicY },
        };
        using (var key = ECDsa.Create(parameters))
            return key.VerifyData(body, signature, HashAlgorithmName.SHA256);
    }
}
