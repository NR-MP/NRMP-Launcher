using System;
using System.Collections.Generic;
using System.Globalization;
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

// gets releases from the github release, makes the game folder match a manifest
internal sealed class Updater
{
    public const string DefaultUrl = "https://github.com/NR-MP/NRMP-MOD/releases/latest/download/";
    private const int Attempts = 3;
    private const string InstalledList = "BepInEx/nrmp-files.txt";
    // the launcher has its own repo, the mod release only says which one should be running
    private const string LauncherUrl = "https://github.com/NR-MP/NRMP-Launcher/releases/latest/download/NightRunnersLauncher.exe";
    private const int MaxManifestBytes = 4 << 20;
    private const int MaxStatusBytes = 64 << 10;
    private const int MaxNames = 64;

    private readonly string _url;
    private readonly string _game;

    public Updater(string url, string gameDir)
    {
        _url = url;
        _game = gameDir;
    }

    // the server keeps its status in a folder named after the join key, so you need the key to find it
    public static string Token(string joinKey)
    {
        using (var sha = SHA256.Create())
            return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes("nrmp-updates:" + joinKey))).Substring(0, 32);
    }

    // one manifest per game build, named after its GameAssembly.dll. null = no release for this build
    public Manifest FetchManifest()
    {
        Manifest manifest;
        try
        {
            var game = HashFile(Path.Combine(_game, "GameAssembly.dll")).Substring(0, 16);
            manifest = Manifest.Parse(Fetch(_url + "manifest-" + game, MaxManifestBytes));
        }
        catch (WebException e) when ((e.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        // a real old manifest is still signed, so someone in the middle could hand out a release with a hole in it.
        // never go back past the newest one this pc has seen
        var newest = NewestSeen();
        if (manifest.Issued < newest)
            throw new InvalidDataException("older than a release this launcher already had");
        if (manifest.Issued > newest)
            RememberNewest(manifest.Issued);
        return manifest;
    }

    private static string NewestFile => Path.Combine(SelfInstall.Folder, "newest-release.txt");

    private static long NewestSeen()
    {
        try
        {
            return File.Exists(NewestFile) && long.TryParse(File.ReadAllText(NewestFile).Trim(), out var seen) ? seen : 0;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static void RememberNewest(long issued)
    {
        try
        {
            Directory.CreateDirectory(SelfInstall.Folder);
            File.WriteAllText(NewestFile, issued.ToString());
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
        }
    }

    // from the server in the player's own cfg, plain http on its address. not signed, so only numbers and a
    // short list of plain names get through
    public static ServerStatus FetchStatus(string host, string joinKey)
    {
        var lines = Encoding.UTF8.GetString(Fetch($"http://{host}/u/{Token(joinKey)}/status", MaxStatusBytes)).Split('\n');
        var status = new ServerStatus { Online = lines[0] == "online" };
        if (!status.Online || lines.Length < 3)
            return status;
        int.TryParse(lines[1], out status.Players);
        int.TryParse(lines[2], out status.MaxPlayers);
        status.Players = Math.Max(0, Math.Min(status.Players, MaxNames));
        status.MaxPlayers = Math.Max(0, Math.Min(status.MaxPlayers, MaxNames));
        status.Names = lines.Skip(3).Select(PlainName).Where(n => n.Length > 0).Take(MaxNames).ToArray();
        return status;
    }

    private static string PlainName(string name)
    {
        // control and hidden characters out, those can flip or hide text
        var plain = new string(name.Where(c => char.GetUnicodeCategory(c) != UnicodeCategory.Control
            && char.GetUnicodeCategory(c) != UnicodeCategory.Format).ToArray()).Trim();
        if (plain.Length <= 32)
            return plain;
        return plain.Substring(0, char.IsHighSurrogate(plain[31]) ? 31 : 32);
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
        // no C1 install, nothing to fix. not in nrmp-files.txt either, so we never delete their files
        foreach (var patch in manifest.Patches)
        {
            var path = Full(patch.Path);
            if (File.Exists(Full(patch.Needs)) && (!File.Exists(path) || HashFile(path) != patch.Hash))
                wanted.Add(patch);
        }
        return wanted;
    }

    public void Apply(Manifest manifest, Action<string, double> progress)
    {
        progress("Checking your files", -1);
        CleanLeftovers(manifest);
        var wanted = Outdated(manifest);
        if (manifest.PackName != null)
        {
            if (wanted.Count > 0 && wanted.All(f => manifest.Loose.ContainsKey(f.Hash)))
                ApplyLoose(manifest, wanted, progress);
            else if (wanted.Count > 0)
                ApplyPack(manifest, wanted, progress);
        }
        else
        {
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
        }

        RemoveDropped(manifest);
        BepInExConfig.KeepShippedInterop(_game);
    }

    // only the mod dll changed, so just that instead of 60 mb of zip
    private void ApplyLoose(Manifest manifest, List<ManifestFile> wanted, Action<string, double> progress)
    {
        var total = wanted.Sum(f => f.Size);
        long done = 0;
        foreach (var file in wanted)
        {
            var start = done;
            Retry(() =>
            {
                done = start;
                Save(_url + manifest.Loose[file.Hash], file.Hash, file.Size, Full(file.Path), false, read =>
                {
                    done += read;
                    progress($"Downloading {done / (1024 * 1024)} of {total / (1024 * 1024)} MB", (double)done / total);
                });
            });
        }
    }

    // one zip per game version on github. the zip is checked first, then every file out of it on its own,
    // and only what's out of date gets taken out
    private void ApplyPack(Manifest manifest, List<ManifestFile> wanted, Action<string, double> progress)
    {
        var pack = Path.Combine(SelfInstall.Folder, "release.zip");
        try
        {
            Retry(() =>
            {
                long done = 0;
                Save(_url + manifest.PackName, manifest.PackHash, manifest.PackSize, pack, false, read =>
                {
                    done += read;
                    progress($"Downloading {done / (1024 * 1024)} of {manifest.PackSize / (1024 * 1024)} MB", (double)done / manifest.PackSize);
                });
            });
            progress("Installing", -1);
            using (var stream = File.OpenRead(pack))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (var file in wanted)
                {
                    // C1 fixes sit in their own folder in the zip, so unzipping it by hand never puts array's files in
                    var name = file.Needs == null ? file.Path : "c1-fixes/" + file.Path;
                    var entry = zip.GetEntry(name) ?? throw new InvalidDataException($"{name} isn't in the release zip");
                    using (var input = entry.Open())
                        Write(input, file.Hash, file.Size, Full(file.Path), _ => { });
                }
            }
        }
        finally
        {
            TryDelete(pack);
        }
    }

    // half downloads and files windows wouldn't let go of last time, only next to files a release puts there
    private void CleanLeftovers(Manifest manifest)
    {
        var folders = manifest.Files.Concat(manifest.Patches)
            .Select(f => TryFull(f.Path))
            .Where(p => p != null)
            .Select(Path.GetDirectoryName)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var leftover in Directory.GetFiles(folder, "*.nrmp").Concat(Directory.GetFiles(folder, "*.nrmp-old")))
                TryDelete(leftover);
        }
    }

    public byte[] DownloadLauncher(Manifest manifest)
    {
        var path = Path.Combine(SelfInstall.Folder, "launcher.new");
        try
        {
            if (manifest.PackName != null)
                Retry(() => Save(LauncherUrl, manifest.LauncherHash, manifest.LauncherSize, path, false, _ => { }));
            else
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
            // a line that isn't a plain game folder path is left alone, whoever put it there
            foreach (var old in File.ReadAllLines(listPath))
            {
                if (old.Length > 0 && !kept.Contains(old) && TryFull(old) is string path)
                    File.Delete(path);
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(listPath));
        File.WriteAllLines(listPath, kept);
    }

    private void Download(string hash, long size, string target, Action<long> onRead) =>
        Save(_url + $"{hash}.gz", hash, size, target, true, onRead);

    private static void Save(string url, string hash, long size, string target, bool gzipped, Action<long> onRead)
    {
        using (var response = Request(url).GetResponse())
        using (var body = response.GetResponseStream())
        using (var input = gzipped ? new GZipStream(body, CompressionMode.Decompress) : body)
            Write(input, hash, size, target, onRead);
    }

    private static void Write(Stream input, string hash, long size, string target, Action<long> onRead)
    {
        var partial = target + ".nrmp";
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        using (var sha = SHA256.Create())
        {
            try
            {
                using (var output = File.Create(partial))
                {
                    // the hash only checks out at the end, so stop a file that's already bigger than it should be
                    // before it fills the disk
                    var buffer = new byte[81920];
                    long written = 0;
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        written += read;
                        if (written > size)
                        {
                            throw new InvalidDataException($"{Path.GetFileName(target)} is bigger than its release");
                        }
                        sha.TransformBlock(buffer, 0, read, null, 0);
                        output.Write(buffer, 0, read);
                        onRead(read);
                    }
                }
            }
            catch
            {
                // a dropped connection or a file too big would leave a half file behind
                TryDelete(partial);
                throw;
            }
            sha.TransformFinalBlock(new byte[0], 0, 0);
            if (Hex(sha.Hash) != hash)
            {
                File.Delete(partial);
                throw new InvalidDataException($"{Path.GetFileName(target)} doesn't match its release");
            }
        }
        Swap(partial, target);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
        }
    }

    // a file windows hasn't finished deleting still blocks the name, so move it out of the way first
    private static void Swap(string from, string to)
    {
        if (File.Exists(to))
        {
            var aside = $"{to}.{DateTime.Now.Ticks}.nrmp-old";
            File.Move(to, aside);
            try
            {
                File.Delete(aside);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }
        File.Move(from, to);
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
            catch (Exception e) when (attempt < Attempts && (e is WebException || e is InvalidDataException
                || e is IOException && e.InnerException is System.Net.Sockets.SocketException))
            {
            }
        }
    }

    private static byte[] Fetch(string url, int maxBytes)
    {
        using (var response = Request(url).GetResponse())
        using (var stream = response.GetResponseStream())
        using (var buffer = new MemoryStream())
        {
            var chunk = new byte[16384];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                    throw new InvalidDataException($"{Path.GetFileName(new Uri(url).AbsolutePath)} is too big");
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
    }

    private static HttpWebRequest Request(string url)
    {
        var request = WebRequest.CreateHttp(url);
        request.Timeout = 15000;
        request.ReadWriteTimeout = 30000;
        request.Proxy = null;
        return request;
    }

    // checked again here so nothing the launcher writes or deletes can end up outside the game folder
    private string Full(string relative)
    {
        var root = Path.GetFullPath(_game).TrimEnd('\\') + "\\";
        var full = Path.GetFullPath(Path.Combine(root, Manifest.SafePath(relative).Replace('/', '\\')));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"bad path {relative}");
        return full;
    }

    private string TryFull(string relative)
    {
        try
        {
            return Full(relative);
        }
        catch (Exception e) when (e is InvalidDataException || e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
        {
            return null;
        }
    }

    public static string HashFile(string path)
    {
        using (var sha = SHA256.Create())
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            return Hex(sha.ComputeHash(stream));
    }

    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
}
