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
        "%LOCALAPPDATA%\\NightRunnersMP, and its two shortcuts. Nothing else.\n\n" +
        "When the launcher Connects to: It connects only to nrmp-eu-1.duckdns.org, which is 128.140.117.115 (NRMP main server and file server Located in germany), for updates and the server status. It sends a hash of your invite code to be able to download the mod and nothing about you or your PC.\n\n" +
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
    private readonly Button _discord = DiscordButton();
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
    private Updater _updater;

    public MainForm()
    {
        Text = "Night-Runners MultiPlayer";
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
        Place(_discord, Gutter, top + 104, width, 34);
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
            Manifest manifest = null;
            Updater updater = null;
            while (manifest == null)
            {
                if (_key == null && !AskForKey(null))
                {
                    Say("You need an invite code to install the Mod Files.");
                    return;
                }
                Say("Looking for updates");
                updater = new Updater(Updater.DefaultUrl, _key, _game);
                manifest = await Task.Run(updater.FetchManifest);
                if (manifest == null && !AskForKey("That invite code wasn't accepted."))
                    return;
            }

            ModConfig.WriteKey(_game, _key);
            ModConfig.Write(_game, manifest.Host, manifest.Port);
            _version.Text = $"Mod {manifest.Version}";
            _updater = updater;
            await StartServerChecks(updater);

            var files = await Task.Run(() => updater.Outdated(manifest));
            var launcher = await Task.Run(() => LauncherOutdated(manifest));
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
            if (!await Task.Run(() => updater.IsRightGame(manifest)) && !ConfirmOtherGame())
            {
                Say("This folder has a different game version.");
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
        catch (Exception e) when (e is WebException || e is IOException || e is UnauthorizedAccessException)
        {
            Offline(e.Message);
        }
        catch (InvalidDataException e)
        {
            Offline($"The update failed its safety check ({e.Message}) and was not installed.");
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

    private async Task StartServerChecks(Updater updater)
    {
        _statusUpdater = updater;
        await RefreshServer();
        _statusTimer.Start();
    }

    private Updater _statusUpdater;

    private async Task RefreshServer()
    {
        ServerStatus status;
        try
        {
            status = await Task.Run(_statusUpdater.FetchStatus);
        }
        catch (Exception e) when (e is WebException || e is IOException || e is FormatException)
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
        if (!installing && _update == null && _play.Enabled)
            StartGame();
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

    private bool AskForKey(string problem)
    {
        using (var dialog = new InviteDialog(problem))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Code.Length == 0)
                return false;
            _key = dialog.Code;
            return true;
        }
    }

    private bool ConfirmOtherGame() => MessageBox.Show(this,
        "This is a different build of the game than the mod was made for, and it will crash no doubt....\n\nInstall anyway?",
        "Different game version", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;

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

    private static Button DiscordButton()
    {
        var button = new Button
        {
            Text = "NRMP DISCORD",
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

    private sealed class InviteDialog : Form
    {
        private readonly TextBox _box = new TextBox();

        public string Code => _box.Text.Trim();

        public InviteDialog(string problem)
        {
            Text = "Invite code";
            ClientSize = new Size(380, 130);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Back;
            ForeColor = Bright;

            var prompt = new Label
            {
                Text = problem ?? "Paste the invite code from your host.",
                Bounds = new Rectangle(14, 12, 352, 22),
            };
            _box.Bounds = new Rectangle(14, 42, 352, 26);
            _box.BackColor = Idle;
            _box.ForeColor = Bright;
            _box.BorderStyle = BorderStyle.FixedSingle;
            var ok = DialogButton("OK", DialogResult.OK, new Rectangle(204, 86, 78, 30));
            var cancel = DialogButton("Cancel", DialogResult.Cancel, new Rectangle(288, 86, 78, 30));
            Controls.AddRange(new Control[] { prompt, _box, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkTitleBar.Apply(this);
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
