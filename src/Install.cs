using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace NightRunnersMP.Launcher;

// installs the launcher itself. per user, so never any admin prompt
internal static class SelfInstall
{
    public static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NightRunnersMP");

    private static string Installed => Path.Combine(Folder, "NightRunnersLauncher.exe");
    private static string SettingsFile => Path.Combine(Folder, "game-folder.txt");

    public static string GameFolder
    {
        get
        {
            var path = File.Exists(SettingsFile) ? File.ReadAllText(SettingsFile).Trim() : null;
            return path != null && GameFinder.IsGame(path) ? path : null;
        }
        set => File.WriteAllText(SettingsFile, value);
    }

    // true if this run was the installer and the installed copy took over
    public static bool Run()
    {
        var self = Application.ExecutablePath;
        if (string.Equals(Path.GetFullPath(self), Installed, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(self + ".old");
            return false;
        }

        var question = $"Install Night-Runners Multiplayer to\n{Folder}\n\nThis copies the launcher there and adds a desktop and a Start menu shortcut. No admin rights needed.";
        if (MessageBox.Show(question, "Night-Runners Multiplayer", MessageBoxButtons.YesNo) != DialogResult.Yes)
            return true;

        Directory.CreateDirectory(Folder);
        File.Copy(self, Installed, true);
        Shortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Shortcut(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
        Process.Start(Installed);
        return true;
    }

    // windows won't overwrite a running exe but renaming one is fine
    public static void Replace(byte[] newExe)
    {
        var self = Application.ExecutablePath;
        TryDelete(self + ".old");
        File.Move(self, self + ".old");
        File.WriteAllBytes(self, newExe);
        Process.Start(self);
        Environment.Exit(0);
    }

    private static void Shortcut(string folder)
    {
        var link = (IShellLink)new ShellLink();
        link.SetPath(Installed);
        link.SetWorkingDirectory(Folder);
        link.SetIconLocation(Installed, 0);
        ((IPersistFile)link).Save(Path.Combine(folder, "Night-Runners Multiplayer.lnk"), true);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    // windows' own shortcut interface. only the slots i call have real signatures, the rest hold their place in the vtable
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        void GetPath();
        void GetIDList();
        void SetIDList();
        void GetDescription();
        void SetDescription();
        void GetWorkingDirectory();
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments();
        void SetArguments();
        void GetHotkey();
        void SetHotkey();
        void GetShowCmd();
        void SetShowCmd();
        void GetIconLocation();
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int icon);
        void SetRelativePath();
        void Resolve();
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal static class GameFinder
{
    public const string Exe = "NIGHT-RUNNERS PRIVATE ALPHA.exe";

    public static bool IsGame(string folder) =>
        File.Exists(Path.Combine(folder, Exe)) && File.Exists(Path.Combine(folder, "GameAssembly.dll"));

    public static bool IsRunning() =>
        Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Exe)).Length > 0;
}

// only touches [Server], rest of the settings stay theirs
internal static class ModConfig
{
    private const string RelativePath = @"BepInEx\config\com.nightrunners.multiplayer.cfg";

    private static string PathIn(string gameDir) => Path.Combine(gameDir, RelativePath);

    public static string ReadKey(string gameDir)
    {
        var path = PathIn(gameDir);
        if (!File.Exists(path))
            return null;
        var match = Regex.Match(File.ReadAllText(path), @"^JoinKey *= *(\S+)", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static void WriteKey(string gameDir, string key) => Set(gameDir, "JoinKey", key);

    public static void Write(string gameDir, string host, int port)
    {
        Set(gameDir, "Host", host);
        Set(gameDir, "Port", port.ToString());
    }

    private static void Set(string gameDir, string name, string value)
    {
        var path = PathIn(gameDir);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var text = File.Exists(path) ? File.ReadAllText(path) : "";
        var line = new Regex($@"^{name} *=.*?(\r?)$", RegexOptions.Multiline);
        if (line.IsMatch(text))
            text = line.Replace(text, m => $"{name} = {value}{m.Groups[1].Value}");
        else if (Regex.IsMatch(text, @"^\[Server\]", RegexOptions.Multiline))
            text = Regex.Replace(text, @"^\[Server\]\r?$", m => $"{m.Value.TrimEnd('\r')}\r\n{name} = {value}\r", RegexOptions.Multiline);
        else
            text += $"\r\n[Server]\r\n{name} = {value}\r\n";
        File.WriteAllText(path, text);
    }
}
