using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace NightRunnersMP.Launcher;

// a problem found before installing or playing, with what the player can do about it
internal sealed class Problem
{
    public string Text;
    // true = no point going on, false = it might still work
    public bool Blocks;

    public Problem(string text, bool blocks)
    {
        Text = text;
        Blocks = blocks;
    }
}

// looks at the game folder before we write to it or start the game, so a known problem gets a fix instead of a crash
internal static class Preflight
{
    private const long SpareBytes = 50L << 20;

    public const string ShortPathTip = @"Move the game to a simple folder like C:\Games\Night-Runners, then press Change folder.";

    // before INSTALL or UPDATE
    public static List<Problem> BeforeInstall(string game, long bytes)
    {
        var problems = PathProblems(game);

        if (!CanWrite(game))
            problems.Add(new Problem(
                "Windows won't let the launcher write to the game folder. If the game is in Program Files, move it. " +
                "If your antivirus has Controlled folder access or ransomware protection, allow NightRunnersLauncher.exe.\n" + ShortPathTip, true));

        var free = FreeBytes(game);
        if (free >= 0 && free < bytes + SpareBytes)
            problems.Add(new Problem(
                $"The {Drive(game)} drive only has {free >> 20} MB free and this needs about {(bytes + SpareBytes) >> 20} MB. Free up some space and try again.", true));

        return problems;
    }

    // before PLAY. sameGame = GameAssembly.dll is the build this release was made for
    public static List<Problem> BeforePlay(string game, bool sameGame)
    {
        var problems = PathProblems(game);
        if (!sameGame)
            problems.Add(WrongGame);
        return problems;
    }

    public static readonly Problem WrongGame = new Problem(
        "Your game is a different build than this mod release was made for. Usually the game just updated and the mod hasn't caught up yet, " +
        "or the folder has an older copy. It will most likely crash.\n" +
        "Check the NRMP Discord for which game build to use, or wait for a mod update.", false);

    private static List<Problem> PathProblems(string game)
    {
        var problems = new List<Problem>();
        // unity and the bepinex loader both trip over paths like that, the game just closes with no error
        if (game.Any(c => c > 127))
            problems.Add(new Problem(
                $"The game folder path has letters outside plain English ({game}). The mod loader can fail to start from a path like that.\n" + ShortPathTip, false));
        if (game.Length > 160)
            problems.Add(new Problem("The game folder path is very long, and some mod files may not fit under Windows' path limit.\n" + ShortPathTip, false));
        if (game.IndexOf(@"\OneDrive", StringComparison.OrdinalIgnoreCase) >= 0)
            problems.Add(new Problem(
                "The game is inside OneDrive. OneDrive can keep mod files online only, or lock them while it syncs, and the mod then fails to load.\n" + ShortPathTip, false));
        return problems;
    }

    private static bool CanWrite(string game)
    {
        var probe = Path.Combine(game, $"nrmp-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static long FreeBytes(string game)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(game))).AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException)
        {
            return -1;
        }
    }

    public static string Drive(string game)
    {
        try
        {
            return Path.GetPathRoot(Path.GetFullPath(game)).TrimEnd('\\');
        }
        catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
        {
            return "game";
        }
    }

    // true = nothing found, or they chose to go on anyway
    public static bool Confirm(IWin32Window owner, List<Problem> problems, string goOn)
    {
        if (problems.Count == 0)
            return true;
        var text = string.Join("\n\n", problems.Select(p => p.Text));
        Diagnostics.Log("preflight: " + text.Replace("\n", " "));
        if (problems.Any(p => p.Blocks))
        {
            MessageBox.Show(owner, text, "Can't go on yet", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        return MessageBox.Show(owner, $"{text}\n\n{goOn} anyway?", "Heads up", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
    }
}

// the launcher's own log, plain words for errors, and the logs zip for the Discord
internal static class Diagnostics
{
    private const long MaxLogBytes = 256 << 10;
    private const long MaxGameLogBytes = 4L << 20;

    public static string LogFile => Path.Combine(SelfInstall.Folder, "launcher.log");

    public static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(SelfInstall.Folder);
            if (File.Exists(LogFile) && new FileInfo(LogFile).Length > MaxLogBytes)
            {
                var old = Path.Combine(SelfInstall.Folder, "launcher.old.log");
                File.Delete(old);
                File.Move(LogFile, old);
            }
            File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}\r\n");
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
        }
    }

    // what went wrong, in words someone can act on. the raw exception goes to the log
    public static string Explain(Exception e, string game)
    {
        Log("error: " + e);
        if (e is InvalidDataException)
            return $"The update failed its safety check ({e.Message}) and was not installed. Try again, and tell us in the Discord if it keeps happening.";
        if (e is WebException web)
            return ExplainWeb(web);
        if (e is PathTooLongException)
            return "A mod file path is too long for Windows. " + Preflight.ShortPathTip;
        if (e is UnauthorizedAccessException)
            return "Windows blocked writing to the game folder. If the game is in Program Files, move it. " +
                "Antivirus Controlled folder access can also block it, allow NightRunnersLauncher.exe there.";
        if (e is DirectoryNotFoundException || e is FileNotFoundException)
            return "A file went missing while installing. If your antivirus quarantined a mod file, restore it or add the game folder as an exception, then try again.";
        if (e is IOException && e.InnerException is SocketException)
            return "The connection to the update server dropped. Check your internet and try again.";
        if (e is IOException)
        {
            switch (e.HResult & 0xFFFF)
            {
                case 39:
                case 112:
                    return $"The {Preflight.Drive(game ?? "")} drive is full. Free up some space and try again.";
                case 32:
                case 33:
                    return "A mod file is in use. Close the game, and anything else that could have it open, then try again.";
            }
        }
        return e.Message;
    }

    private static string ExplainWeb(WebException e)
    {
        switch (e.Status)
        {
            case WebExceptionStatus.NameResolutionFailure:
                return "Couldn't find the update server. Check your internet connection.";
            case WebExceptionStatus.TrustFailure:
            case WebExceptionStatus.SecureChannelFailure:
                return "The secure connection to the update server failed. Check that your PC's date and time are right, " +
                    "and turn off HTTPS or web scanning in your antivirus if it has it.";
            case WebExceptionStatus.ProtocolError:
                var code = (int)((e.Response as HttpWebResponse)?.StatusCode ?? 0);
                if (code >= 500)
                    return $"The update server had a problem (error {code}). Try again in a few minutes.";
                if (code == 404)
                    return "The update server is missing a file from this release, a new one may be going up right now. Try again in a few minutes.";
                return $"The update server said no (error {code}). Try again later, or ask in the Discord.";
            default:
                return "Couldn't reach the update server. It may be down, try again in a few minutes and check the NRMP Discord.";
        }
    }

    // a zip on the desktop with what we always ask for in support, join key, server address and windows user name taken out
    public static string PackLogs(string game, string key, Func<string> report)
    {
        var name = $"NRMP-logs-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
        var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name);
        var secrets = Secrets(key);
        // logs get posted in the discord, the server address stays in the channel it came from
        if (ModConfig.ReadHost(game) is string host)
            secrets.Add(host);

        using (var zip = ZipFile(target))
        {
            Add(zip, "report.txt", report(), secrets);
            Add(zip, "launcher.log", ReadTail(LogFile), secrets);
            if (game != null)
            {
                Add(zip, "LogOutput.log", ReadTail(Path.Combine(game, @"BepInEx\LogOutput.log")), secrets);
                Add(zip, "BepInEx.cfg", ReadTail(Path.Combine(game, @"BepInEx\config\BepInEx.cfg")), secrets);
                Add(zip, "com.nightrunners.multiplayer.cfg", ReadTail(Path.Combine(game, @"BepInEx\config\com.nightrunners.multiplayer.cfg")), secrets);
            }
            var player = PlayerLog();
            if (player != null)
                Add(zip, "Player.log", ReadTail(player), secrets);
        }
        Log("packed logs " + name);
        return target;
    }

    private static ZipArchive ZipFile(string path) => new ZipArchive(File.Create(path), ZipArchiveMode.Create);

    private static void Add(ZipArchive zip, string name, string text, List<string> secrets)
    {
        if (text == null)
            return;
        var entry = zip.CreateEntry(name);
        using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            writer.Write(Redact(text, secrets));
    }

    // longest first, so the profile path goes before the bare user name inside it
    private static List<string> Secrets(string key)
    {
        var secrets = new List<string>();
        if (!string.IsNullOrEmpty(key))
        {
            secrets.Add(key);
            secrets.Add(Updater.Token(key));
        }
        return secrets;
    }

    public static string Redact(string text, List<string> secrets)
    {
        foreach (var secret in secrets.Where(s => s.Length >= 4).OrderByDescending(s => s.Length))
            text = Replace(text, secret, "<invite code>");
        text = Regex.Replace(text, @"^(\s*JoinKey\s*=).*$", "$1 <invite code>", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"^(\s*Host\s*=).*$", "$1 <server>", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 3)
            text = Replace(text, profile, "%USERPROFILE%");
        var user = Environment.UserName;
        if (user.Length >= 3)
            text = Regex.Replace(text, $@"(?<![A-Za-z0-9]){Regex.Escape(user)}(?![A-Za-z0-9])", "<user>", RegexOptions.IgnoreCase);
        return text;
    }

    private static string Replace(string text, string find, string with) =>
        Regex.Replace(text, Regex.Escape(find), with.Replace("$", "$$"), RegexOptions.IgnoreCase);

    // the end of a log is where the crash is, and a log can get huge
    private static string ReadTail(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var cut = stream.Length > MaxGameLogBytes;
                if (cut)
                    stream.Seek(-MaxGameLogBytes, SeekOrigin.End);
                using (var reader = new StreamReader(stream))
                    return (cut ? "[start of the log cut off]\r\n" : "") + reader.ReadToEnd();
            }
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return $"[couldn't read {Path.GetFileName(path)}: {e.Message}]";
        }
    }

    // unity keeps it in LocalLow\<company>\<product>, we don't know the company name for sure so look for the product
    private static string PlayerLog()
    {
        try
        {
            var localLow = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow");
            if (!Directory.Exists(localLow))
                return null;
            return Directory.GetDirectories(localLow)
                .SelectMany(company => Directory.GetDirectories(company))
                .Where(product => Path.GetFileName(product).IndexOf("NIGHT", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(product => Path.Combine(product, "Player.log"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void ShowInExplorer(string file) => Process.Start("explorer.exe", $"/select,\"{file}\"");
}
