using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NightRunnersMP.Launcher;


internal sealed class MainForm : Form
{
    private const string PatreonUrl = "https://www.patreon.com/NightRunnersGame";
    private const string DiscordUrl = "https://discord.gg/ERk9SxD4Ec";
    private const string Credits =
        "NIGHT-RUNNERS is made by PlanetJem. All credit for the game goes to them.\n" +
        "Please support the game on Patreon.\n\n" +
        "Multiplayer mod by MILLIYEN.\n" +
        "It is not an official part of the game.\n\n" +
        "Built with BepInEx, Il2CppInterop and LiteNetLib.";
    private const string WhatItDoes =
        "When the launcher Reads: It reads the game folder you picked, and its settings.\n" +
        "When the launcher Writes: Writes the mod files inside the game folder, Mod settings in " +
        "%LOCALAPPDATA%\\NightRunnersMP (with the date of the newest update it has seen, so nobody can hand it an older one), its own log launcher.log, the release zip while an update downloads (deleted right after), and its two shortcuts. Before installing it writes and deletes one empty test file in the game folder to see if Windows lets it. After an update it sets UpdateInteropAssemblies to false in BepInEx.cfg, so BepInEx doesn't try to rebuild the mod's files and break them. If you installed array's C1 map yourself and the release has array's 6 fix files, it swaps those in. Nothing else.\n\n" +
        "SHARE LOGS: only when you press it, it reads BepInEx\\LogOutput.log, the two BepInEx settings files, the game's Player.log and its own log, " +
        "takes out your invite code, the server address and your Windows user name, and zips them onto your Desktop. It sends nothing, you post the zip yourself.\n\n" +
        "When the launcher Connects to: Updates come from the NRMP-MOD release on GitHub and its own updates from the NRMP-Launcher release, so github.com and GitHub's download servers (objects.githubusercontent.com and release-assets.githubusercontent.com). Anyone can download those, it sends nothing about you or your PC. " +
        "For the player count it asks the server you put in BepInEx\\config\\com.nightrunners.multiplayer.cfg yourself, over plain http, with a hash of your invite code. No server in there, no player count. It never writes that file.\n\n" +
        "Updates: it checks on its own. Nothing downloads until you press Update, and every update has to match a signature built into this launcher maintained by me MILLIYEN.\n\n" +
        "To remove it, delete that folder and the shortcuts. The mod is the BepInEx folder, winhttp.dll and doorstop_config.ini in the game folder.";
    private const int StatusEveryMs = 30000;
    private const int SplashEveryMs = 40;
    private const float SplashWidth = 250f;
    private const int Gutter = 24;
    private const int LogoFade = 48;

    private static readonly Rectangle SplashArea = new Rectangle(10, 0, 285, 100);
    private static readonly Color Back = Color.FromArgb(14, 15, 18);
    private static readonly Color Bright = Color.FromArgb(232, 232, 235);
    private static readonly Color Muted = Color.FromArgb(128, 131, 140);
    private static readonly Color Accent = Color.FromArgb(214, 40, 52);
    private static readonly Color AccentHover = Color.FromArgb(232, 58, 68);
    private static readonly Color Idle = Color.FromArgb(36, 38, 44);
    private static readonly Color Green = Color.FromArgb(90, 220, 130);
    private static readonly Color Red = Color.FromArgb(240, 95, 90);

    private readonly Image _logo = Image.FromStream(typeof(MainForm).Assembly.GetManifestResourceStream("logo.jpg"));
    private readonly Label _status = Caption(11f, Bright);
    private readonly Label _version = Caption(9f, Muted);
    private readonly Label _server = Caption(9.5f, Muted);
    private readonly ProgressStrip _progress = new ProgressStrip();
    private readonly Button _play = PlayButton();
    private readonly Button _discord = DarkButton("NRMP DISCORD");
    private readonly Button _share = DarkButton("SHARE LOGS");
    private readonly Label _patreon = Link("Support PlanetJem on Patreon");
    private readonly Label _credits = Link("Credits");
    private readonly Label _change = Link("Change folder");
    private readonly Label _about = Link("What this does");
    private readonly ToolTip _tips = new ToolTip();
    private readonly Timer _statusTimer = new Timer { Interval = StatusEveryMs };
    private readonly Timer _splashTimer = new Timer { Interval = SplashEveryMs };
    private readonly string[] _splashes = LoadSplashes();
    private int _splash;
    private readonly Font _splashFont = new Font("Segoe UI", 15f, FontStyle.Bold);

    private string _game;
    private string _key;
    private bool _busy;
    private Manifest _update;
    private Manifest _manifest;
    private long _bytes;
    private Updater _updater;

    public MainForm()
    {
        Text = "Night-Runners Multiplayer";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        ClientSize = new Size(_logo.Width, _logo.Height + 236);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        BackColor = Back;
        Font = new Font("Segoe UI", 10f);

        var width = ClientSize.Width - Gutter * 2;
        var top = _logo.Height;
        Place(_status, Gutter, top - 16, width, 48);
        Place(_progress, Gutter, top + 34, width, 2);
        Place(_play, Gutter, top + 42, width, 54);
        var discordWidth = width * 3 / 5;
        Place(_discord, Gutter, top + 104, discordWidth, 34);
        Place(_share, Gutter + discordWidth + 8, top + 104, width - discordWidth - 8, 34);
        Place(_server, Gutter + 16, top + 148, width - 16, 22);
        Place(_version, ClientSize.Width - Gutter - 130, top + 180, 130, 20);
        PlaceRow(top + 180, _patreon, _credits, _change, _about);

        _version.TextAlign = ContentAlignment.MiddleRight;
        _status.TextAlign = ContentAlignment.BottomLeft;
        _server.AutoEllipsis = true;
        _play.Enabled = false;
        _progress.Visible = false;
        _status.Text = "Starting";
        _server.Text = "Checking server";

        _play.Click += async (_, _) => await Play();
        _change.Click += async (_, _) => await PickFolder();
        _patreon.Click += (_, _) => Process.Start(PatreonUrl);
        _discord.Click += (_, _) => Process.Start(DiscordUrl);
        _share.Click += async (_, _) => await ShareLogs();
        _tips.SetToolTip(_share, "Zips your logs for the Discord, without your invite code or the server address");
        _credits.Click += (_, _) =>
        {
            using (var dialog = new CreditsDialog())
                dialog.ShowDialog(this);
        };
        _about.Click += (_, _) => MessageBox.Show(this, WhatItDoes, "What this launcher does");
        _statusTimer.Tick += async (_, _) => await RefreshServer();
        _splash = new Random().Next(_splashes.Length);
        _splashTimer.Tick += (_, _) => Invalidate(SplashArea);
        _splashTimer.Start();
        Shown += async (_, _) => await Prepare();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkTitleBar.Apply(this);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImageUnscaled(_logo, 0, 0);
        var fade = new Rectangle(0, _logo.Height - LogoFade, ClientSize.Width, LogoFade);
        // one row taller than the fill or the gradient wraps and row 1 goes solid
        var span = new Rectangle(fade.X, fade.Y - 1, fade.Width, fade.Height + 1);
        using (var back = new LinearGradientBrush(span, Color.FromArgb(0, Back), Back, 90f))
            g.FillRectangle(back, fade);

        DrawSplash(g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var dot = new SolidBrush(_serverColor))
            g.FillEllipse(dot, Gutter, _server.Top + 7, 8, 8);
        using (var small = new Font("Segoe UI", 8.5f))
            TextRenderer.DrawText(g, "Game by PlanetJem. Multiplayer is a fan mod, not official.", small,
                new Point(Gutter - 3, _version.Bottom + 6), Muted);
    }

    private Color _serverColor = Muted;

    private async Task Prepare()
    {
        Diagnostics.Log($"launcher {Application.ProductVersion} started");
        _busy = true;
        try
        {
            _game = SelfInstall.GameFolder;
            if (_game == null && !AskForFolder())
            {
                Say("Pick your Night-Runners game folder to install.");
                return;
            }
            SelfInstall.GameFolder = _game;
            _tips.SetToolTip(_change, _game);

            _key = ModConfig.ReadKey(_game);
            await Check();
        }
        finally
        {
            _busy = false;
        }
    }

    private static string[] LoadSplashes()
    {
        using (var reader = new StreamReader(typeof(MainForm).Assembly.GetManifestResourceStream("splashes.txt")))
            return reader.ReadToEnd().Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != '#').ToArray();
    }

    // easter egg, click the logo for the next line
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Y < _logo.Height)
            _splash = (_splash + 1) % _splashes.Length;
    }

    // tilted and breathing next to the logo, long ones just get smaller
    private void DrawSplash(Graphics g)
    {
        var line = _splashes[_splash];
        var size = g.MeasureString(line, _splashFont);
        var scale = Math.Min(1f, SplashWidth / size.Width) * (1f + 0.05f * (float)Math.Abs(Math.Sin(Environment.TickCount / 300.0)));
        var state = g.Save();
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.TranslateTransform(150f, 52f);
        g.RotateTransform(-9f);
        g.ScaleTransform(scale, scale);
        using (var shadow = new SolidBrush(Color.FromArgb(63, 63, 0)))
            g.DrawString(line, _splashFont, shadow, -size.Width / 2 + 2, -size.Height / 2 + 2);
        using (var yellow = new SolidBrush(Color.FromArgb(255, 255, 0)))
            g.DrawString(line, _splashFont, yellow, -size.Width / 2, -size.Height / 2);
        g.Restore(state);
    }

    private bool ModInstalled => File.Exists(Path.Combine(_game, @"BepInEx\plugins\NightRunnersMP\NightRunnersMP.dll"));

    // only looks. nothing downloads until they press the button
    private async Task Check()
    {
        _play.Enabled = false;
        await Guarded(async () =>
        {
            await StartServerChecks();
            Say("Looking for updates");
            var updater = new Updater(Updater.DefaultUrl, _game);
            var manifest = await Task.Run(updater.FetchManifest);
            if (manifest == null)
            {
                Say("There's no NRMP release for this game version yet.");
                return;
            }

            _version.Text = $"Mod {manifest.Version}";
            _manifest = manifest;
            _updater = updater;

            var files = await Task.Run(() => updater.Outdated(manifest));
            var launcher = await Task.Run(() => LauncherOutdated(manifest));
            Diagnostics.Log($"mod {manifest.Version}, {files.Count} files to get, launcher outdated {launcher}");
            if (files.Count == 0 && !launcher)
            {
                Ready("Up to date. Have fun.");
                return;
            }
            Offer(manifest, files.Sum(f => f.Size) + (launcher ? manifest.LauncherSize : 0));
        });
    }

    private void Offer(Manifest manifest, long bytes)
    {
        var word = ModInstalled ? "UPDATE" : "INSTALL";
        var size = Math.Max(1, bytes / (1024 * 1024));
        _update = manifest;
        _bytes = bytes;
        _play.Text = word;
        _play.Enabled = true;
        Say(ModInstalled ? $"Update available, {size} MB. Press {word} to get it." : $"Ready to install, {size} MB. Press {word} to start.");
    }

    private async Task Install()
    {
        var manifest = _update;
        var updater = _updater;
        _play.Enabled = false;
        await Guarded(async () =>
        {
            var problems = await Task.Run(() => Preflight.BeforeInstall(_game, _bytes));
            if (!await Task.Run(() => updater.IsRightGame(manifest)))
                problems.Add(Preflight.WrongGame);
            if (!Preflight.Confirm(this, problems, "Install"))
            {
                Say(problems.Any(p => p.Blocks) ? "Fix the problem above, then press " + _play.Text + " again." : "Not installed.");
                _play.Enabled = true;
                return;
            }
            while (GameFinder.IsRunning())
            {
                Say("Close the game so the update can finish.");
                await Task.Delay(2000);
            }

            var installed = ModInstalled;
            _progress.Visible = true;
            await Task.Run(() => updater.Apply(manifest, (text, fraction) => BeginInvoke((Action)(() =>
            {
                _status.Text = text;
                _progress.Fraction = fraction;
            }))));
            _progress.Visible = false;
            // last, this one restarts the launcher
            if (await Task.Run(() => LauncherOutdated(manifest)))
            {
                Say("Updating the launcher");
                SelfInstall.Replace(await Task.Run(() => updater.DownloadLauncher(manifest)));
            }
            Ready(installed ? "Up to date. Have fun." : "Installed. Have fun.");
        });
    }

    private async Task Guarded(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception e) when (e is WebException || e is IOException || e is UnauthorizedAccessException || e is InvalidDataException)
        {
            Offline(Diagnostics.Explain(e, _game));
        }
    }

    private void Offline(string message)
    {
        _progress.Visible = false;
        if (ModInstalled)
        {
            Ready($"Couldn't update, your current version still works. {message}");
            return;
        }
        Say($"Couldn't install. {message}");
        _play.Enabled = _update != null;
    }

    private static bool LauncherOutdated(Manifest manifest) =>
        Updater.HashFile(Application.ExecutablePath) != manifest.LauncherHash;

    private async Task StartServerChecks()
    {
        await RefreshServer();
        _statusTimer.Start();
    }

    // read every time, they might fill in the cfg while the launcher is open
    private async Task RefreshServer()
    {
        var host = ModConfig.ReadHost(_game);
        var key = ModConfig.ReadKey(_game);
        if (host == null || key == null)
        {
            _serverColor = Muted;
            _server.Text = "No server set yet, get it from the NRMP Discord";
            _tips.SetToolTip(_server, @"Host, Port and JoinKey go in BepInEx\config\com.nightrunners.multiplayer.cfg");
            Invalidate();
            return;
        }
        _key = key;
        _tips.SetToolTip(_server, null);

        ServerStatus status;
        try
        {
            status = await Task.Run(() => Updater.FetchStatus(host, key));
        }
        catch (Exception e) when (e is WebException || e is IOException || e is FormatException || e is InvalidDataException || e is UriFormatException)
        {
            status = new ServerStatus();
        }

        _serverColor = status.Online ? Green : Red;
        _server.Text = !status.Online ? "Server offline"
            : status.Players == 0 ? $"Online, 0 of {status.MaxPlayers} players"
            : $"Online, {status.Players} of {status.MaxPlayers} players: {string.Join(", ", status.Names)}";
        Invalidate();
    }

    // launcher can sit open for hours, check again before starting
    private async Task Play()
    {
        if (_busy)
            return;
        _busy = true;
        var installing = _update != null;
        try
        {
            await (installing ? Install() : Check());
        }
        finally
        {
            _busy = false;
        }
        if (!installing && _update == null && _play.Enabled && await ReadyToPlay())
            StartGame();
    }

    // the game can update under the mod, or the folder can be one the loader can't start from
    private async Task<bool> ReadyToPlay()
    {
        var manifest = _manifest;
        var updater = _updater;
        var sameGame = true;
        try
        {
            sameGame = manifest == null || updater == null || await Task.Run(() => updater.IsRightGame(manifest));
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Diagnostics.Log("couldn't hash GameAssembly.dll: " + e.Message);
        }
        var problems = Preflight.BeforePlay(_game, sameGame);
        if (Preflight.Confirm(this, problems, "Play"))
            return true;
        Say("Not started.");
        return false;
    }

    private async Task ShareLogs()
    {
        _share.Enabled = false;
        try
        {
            var status = _status.Text;
            var zip = await Task.Run(() => Diagnostics.PackLogs(_game, _key, () => Report(status)));
            var summary = $"NRMP launcher {Application.ProductVersion}, mod {_manifest?.Version ?? "unknown"}, " +
                $"game build matches: {await Task.Run(GameMatches)}. Last message: {status}";
            Clipboard.SetText(Diagnostics.Redact(summary, new System.Collections.Generic.List<string> { _key ?? "" }));
            Diagnostics.ShowInExplorer(zip);
            if (MessageBox.Show(this,
                    $"Your logs are in {Path.GetFileName(zip)} on your Desktop. Your invite code, the server address and your Windows user name are taken out, and nothing was sent anywhere.\n\n" +
                    "Post the zip in the NRMP Discord and say what happened. A short summary is on your clipboard too.\n\nOpen the Discord now?",
                    "Logs ready", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                Process.Start(DiscordUrl);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ExternalException)
        {
            Diagnostics.Log("share logs failed: " + e);
            MessageBox.Show(this, $"Couldn't pack the logs: {e.Message}", "Share logs", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _share.Enabled = true;
        }
    }

    private string GameMatches()
    {
        try
        {
            return _manifest == null || _updater == null ? "unknown" : _updater.IsRightGame(_manifest) ? "yes" : "NO";
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return "couldn't read GameAssembly.dll";
        }
    }

    // what we'd ask anyway in support, so nobody has to
    private string Report(string status)
    {
        var lines = new System.Collections.Generic.List<string>
        {
            "Night-Runners Multiplayer launcher report",
            $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}",
            $"Launcher: {Application.ProductVersion}",
            $"Windows: {Environment.OSVersion}, {(Environment.Is64BitOperatingSystem ? "64" : "32")} bit",
            $"Game folder: {_game ?? "not picked"}",
            $"Mod release: {_manifest?.Version ?? "unknown"}",
            $"Mod installed: {(_game != null && ModInstalled ? "yes" : "no")}",
            $"Game build matches the mod: {GameMatches()}",
            $"Last message: {status}",
        };
        if (_game != null)
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_game)));
                lines.Add($"Free space on {drive.Name}: {drive.AvailableFreeSpace >> 20} MB");
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException)
            {
            }
            lines.AddRange(Preflight.BeforePlay(_game, true).Select(p => "Path check: " + p.Text.Replace("\n", " ")));
        }
        if (_manifest != null && _updater != null)
        {
            try
            {
                var off = _updater.Outdated(_manifest);
                lines.Add(off.Count == 0 ? "Mod files: all match the release" : "Mod files missing or changed:");
                lines.AddRange(off.Select(f => "  " + f.Path));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException)
            {
                lines.Add("Mod files: couldn't check, " + e.Message);
            }
        }
        return string.Join("\r\n", lines) + "\r\n";
    }

    private void StartGame()
    {
        if (Process.GetProcessesByName("steam").Length == 0)
        {
            try
            {
                Process.Start("steam://open/main");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Say("Open Steam first, multiplayer needs it.");
                return;
            }
        }
        Process.Start(new ProcessStartInfo(Path.Combine(_game, GameFinder.Exe)) { WorkingDirectory = _game });
        Close();
    }

    private async Task PickFolder()
    {
        if (_busy || !AskForFolder())
            return;
        SelfInstall.GameFolder = _game;
        _tips.SetToolTip(_change, _game);
        _key = ModConfig.ReadKey(_game) ?? _key;
        _update = null;
        _busy = true;
        try
        {
            await Check();
        }
        finally
        {
            _busy = false;
        }
    }

    private bool AskForFolder()
    {
        using (var dialog = new OpenFileDialog
        {
            Title = "Find NIGHT-RUNNERS PRIVATE ALPHA.exe",
            Filter = "Night Runners|" + GameFinder.Exe,
        })
        {
            while (dialog.ShowDialog(this) == DialogResult.OK)
            {
                var folder = Path.GetDirectoryName(dialog.FileName);
                if (!GameFinder.IsGame(folder))
                    continue;
                _game = folder;
                return true;
            }
        }
        return false;
    }

    private void Say(string text) => _status.Text = text;

    private void Ready(string text)
    {
        _update = null;
        _status.Text = text;
        _play.Text = "PLAY";
        _play.Enabled = true;
    }

    private void Place(Control control, int x, int y, int width, int height)
    {
        control.Bounds = new Rectangle(x, y, width, height);
        Controls.Add(control);
    }

    private void PlaceRow(int y, params Label[] links)
    {
        var x = Gutter - 3;
        foreach (var link in links)
        {
            var width = link.PreferredWidth;
            Place(link, x, y, width, 20);
            x += width + 10;
        }
    }

    private static Label Caption(float size, Color color) => new Label
    {
        BackColor = Color.Transparent,
        ForeColor = color,
        Font = new Font("Segoe UI", size),
    };

    private static Label Link(string text)
    {
        var link = Caption(9f, Muted);
        link.Text = text;
        link.Cursor = Cursors.Hand;
        link.MouseEnter += (_, _) => link.ForeColor = Bright;
        link.MouseLeave += (_, _) => link.ForeColor = Muted;
        return link;
    }

    private static Button DarkButton(string text)
    {
        var button = new Button
        {
            Text = text,
            BackColor = Idle,
            ForeColor = Bright,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            TabStop = false,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(50, 53, 60);
        button.FlatAppearance.MouseDownBackColor = Idle;
        return button;
    }

    private static Button PlayButton()
    {
        var button = new Button
        {
            Text = "PLAY",
            BackColor = Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = AccentHover;
        button.FlatAppearance.MouseDownBackColor = Accent;
        button.EnabledChanged += (_, _) => button.BackColor = button.Enabled ? Accent : Idle;
        return button;
    }

    private static Button DialogButton(string text, DialogResult result, Rectangle bounds)
    {
        var button = new Button
        {
            Text = text,
            DialogResult = result,
            Bounds = bounds,
            BackColor = Idle,
            ForeColor = Bright,
            FlatStyle = FlatStyle.Flat,
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private sealed class ProgressStrip : Control
    {
        private double _fraction;

        public double Fraction
        {
            set
            {
                _fraction = value;
                Invalidate();
            }
        }

        public ProgressStrip()
        {
            DoubleBuffered = true;
            BackColor = Idle;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_fraction <= 0)
                return;
            using (var fill = new SolidBrush(Accent))
                e.Graphics.FillRectangle(fill, 0, 0, (int)(Width * Math.Min(_fraction, 1)), Height);
        }
    }

    private sealed class CreditsDialog : Form
    {
        public CreditsDialog()
        {
            Text = "Credits";
            ClientSize = new Size(460, 230);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Back;
            ForeColor = Bright;
            Font = new Font("Segoe UI", 10f);

            var text = new Label { Text = Credits, Bounds = new Rectangle(16, 14, 428, 164) };
            var ok = DialogButton("OK", DialogResult.OK, new Rectangle(366, 186, 78, 30));
            Controls.AddRange(new Control[] { text, ok });
            AcceptButton = ok;
            CancelButton = ok;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkTitleBar.Apply(this);
        }
    }

    private static class DarkTitleBar
    {
        private const int ImmersiveDarkMode = 20;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        public static void Apply(Form form)
        {
            var on = 1;
            DwmSetWindowAttribute(form.Handle, ImmersiveDarkMode, ref on, sizeof(int));
        }
    }
}
