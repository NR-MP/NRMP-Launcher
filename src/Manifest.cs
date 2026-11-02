using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NightRunnersMP.Launcher;

internal sealed class ManifestFile
{
    public string Hash;
    public long Size;
    // only seeded once, after that the file is the player's
    public bool Once;
    public string Path;
}

// file list of a release. updates come over plain http so trust nothing until the sig checks out against the key in this exe
internal sealed class Manifest
{
    private static readonly byte[] PublicX = Convert.FromBase64String("bmYhJWytV0/u19a3Z294vrxLNmfKOOTid8CMeBnaInY=");
    private static readonly byte[] PublicY = Convert.FromBase64String("KP7XGC71XVzSQ4bd2X4HJ+T5kSKTB9lwcD/DkumlMlc=");

    public string Version;
    public string GameHash;
    public string LauncherHash;
    public long LauncherSize;
    public string Host;
    public int Port;
    public readonly List<ManifestFile> Files = new List<ManifestFile>();

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

        var manifest = new Manifest();
        foreach (var line in Encoding.UTF8.GetString(body).Split('\n'))
        {
            var parts = line.Split(new[] { ' ' }, 5);
            switch (parts[0])
            {
                case "version": manifest.Version = parts[1]; break;
                case "game": manifest.GameHash = parts[1]; break;
                case "server":
                    manifest.Host = parts[1];
                    manifest.Port = int.Parse(parts[2]);
                    break;
                case "launcher":
                    manifest.LauncherHash = parts[1];
                    manifest.LauncherSize = long.Parse(parts[2]);
                    break;
                case "file":
                    manifest.Files.Add(new ManifestFile
                    {
                        Hash = parts[1],
                        Size = long.Parse(parts[2]),
                        Once = parts[3] == "once",
                        Path = SafePath(parts[4]),
                    });
                    break;
            }
        }
        return manifest;
    }

    private static string SafePath(string path)
    {
        if (System.IO.Path.IsPathRooted(path) || Array.IndexOf(path.Split('/', '\\'), "..") >= 0)
            throw new InvalidDataException($"bad path {path}");
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
