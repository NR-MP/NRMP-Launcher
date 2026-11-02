using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace NightRunnersMP.Launcher;

internal sealed class ServerStatus
{
    public bool Online;
    public int Players;
    public int MaxPlayers;
    public string[] Names = new string[0];
}

// talks to the update host, makes the game folder match a manifest
internal sealed class Updater
{
    public const string DefaultUrl = "https://nrmp-eu-1.duckdns.org/u/";
    private const int Attempts = 3;
    private const string InstalledList = "BepInEx/nrmp-files.txt";

    private readonly string _url;
    private readonly string _game;

    public Updater(string url, string joinKey, string gameDir)
    {
        _url = url + Token(joinKey) + "/";
        _game = gameDir;
    }

    // host folder is named after the join key, so you need the key to find it
    private static string Token(string joinKey)
    {
        using (var sha = SHA256.Create())
            return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes("nrmp-updates:" + joinKey))).Substring(0, 32);
    }

    // null = host doesn't know this key
    public Manifest FetchManifest()
    {
        try
        {
            return Manifest.Parse(Fetch("manifest"));
        }
        catch (WebException e) when ((e.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public ServerStatus FetchStatus()
    {
        var lines = Encoding.UTF8.GetString(Fetch("status")).Split('\n');
        var status = new ServerStatus { Online = lines[0] == "online" };
        if (!status.Online || lines.Length < 3)
            return status;
        status.Players = int.Parse(lines[1]);
        status.MaxPlayers = int.Parse(lines[2]);
        status.Names = new string[Math.Max(0, lines.Length - 4)];
        Array.Copy(lines, 3, status.Names, 0, status.Names.Length);
        return status;
    }

    public bool IsRightGame(Manifest manifest) =>
        HashFile(Path.Combine(_game, "GameAssembly.dll")) == manifest.GameHash;

    public List<ManifestFile> Outdated(Manifest manifest)
    {
        var wanted = new List<ManifestFile>();
        foreach (var file in manifest.Files)
        {
            var path = Full(file.Path);
            if (!File.Exists(path) || (!file.Once && HashFile(path) != file.Hash))
                wanted.Add(file);
        }
        return wanted;
    }

    public void Apply(Manifest manifest, Action<string, double> progress)
    {
        progress("Checking your files", -1);
        var wanted = Outdated(manifest);
        var total = wanted.Sum(f => f.Size);

        long done = 0;
        foreach (var file in wanted)
        {
            var start = done;
            Retry(() =>
            {
                done = start;
                Download(file.Hash, file.Size, Full(file.Path), read =>
                {
                    done += read;
                    progress($"Downloading {done / (1024 * 1024)} of {total / (1024 * 1024)} MB", (double)done / total);
                });
            });
        }

        RemoveDropped(manifest);
    }

    public byte[] DownloadLauncher(Manifest manifest)
    {
        var path = Path.Combine(SelfInstall.Folder, "launcher.new");
        try
        {
            Retry(() => Download(manifest.LauncherHash, manifest.LauncherSize, path, _ => { }));
            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // anything dropped from a release has to go or bepinex keeps loading the old plugin
    private void RemoveDropped(Manifest manifest)
    {
        var listPath = Full(InstalledList);
        var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
            kept.Add(file.Path);
        if (File.Exists(listPath))
        {
            foreach (var old in File.ReadAllLines(listPath))
            {
                if (old.Length > 0 && !kept.Contains(old) && !Path.IsPathRooted(old) && Array.IndexOf(old.Split('/'), "..") < 0)
                    File.Delete(Full(old));
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(listPath));
        File.WriteAllLines(listPath, kept);
    }

    private void Download(string hash, long size, string target, Action<long> onRead)
    {
        var partial = target + ".nrmp";
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        using (var sha = SHA256.Create())
        {
            var request = Request($"files/{hash}.gz");
            using (var response = request.GetResponse())
            using (var gzip = new GZipStream(response.GetResponseStream(), CompressionMode.Decompress))
            using (var output = File.Create(partial))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                {
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    output.Write(buffer, 0, read);
                    onRead(read);
                }
            }
            sha.TransformFinalBlock(new byte[0], 0, 0);
            if (Hex(sha.Hash) != hash)
            {
                File.Delete(partial);
                throw new InvalidDataException($"{Path.GetFileName(target)} doesn't match its release");
            }
        }
        File.Delete(target);
        File.Move(partial, target);
    }

    private static void Retry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception e) when (attempt < Attempts && (e is WebException || e is InvalidDataException))
            {
            }
        }
    }

    private byte[] Fetch(string path)
    {
        using (var response = Request(path).GetResponse())
        using (var stream = response.GetResponseStream())
        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

    private HttpWebRequest Request(string path)
    {
        var request = WebRequest.CreateHttp(_url + path);
        request.Timeout = 15000;
        request.ReadWriteTimeout = 30000;
        request.Proxy = null;
        return request;
    }

    private string Full(string relative) => Path.Combine(_game, relative.Replace('/', '\\'));

    public static string HashFile(string path)
    {
        using (var sha = SHA256.Create())
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            return Hex(sha.ComputeHash(stream));
    }

    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
}
