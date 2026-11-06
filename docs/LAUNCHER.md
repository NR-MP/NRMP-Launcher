# What the launcher does

This is the whole list. If it's not on here, the launcher doesn't do it.

**Reads**
- the game folder you pick, and the mod's settings file in it
- `%LOCALAPPDATA%\NightRunnersMP\game-folder.txt` and `newest-release.txt`
- whether the game and Steam are running, by process name
- how much space is free on the game's drive, before an install or update
- only when you press SHARE LOGS: `BepInEx\LogOutput.log`, `BepInEx.cfg`, the mod's settings file, the game's Unity `Player.log` in `AppData\LocalLow`, and its own `launcher.log`

**Writes**
- `%LOCALAPPDATA%\NightRunnersMP`: the launcher, `game-folder.txt`, `newest-release.txt` (just the date of the newest release it's seen), `launcher.log` (what it checked and any errors, kept under 256 KB with one `launcher.old.log`), `release.zip` while an update downloads (deleted right after), `launcher.new` for a moment during a self update, and the old exe as `.old` until the next start
- a desktop and a Start menu shortcut, after asking on first run
- one empty test file in the game folder before an install or update, deleted right away, to see if Windows lets it write there
- only when you press SHARE LOGS: one `NRMP-logs-<date>.zip` on your Desktop, and a one-line summary on your clipboard. Your invite code, its hash, the server address and your Windows user name are taken out first. Nothing gets uploaded, you post the zip yourself
- mod files inside the game folder, listed in `BepInEx/nrmp-files.txt`, plus its own half-downloaded `.nrmp` temp files, which it clears on the next update
- one line in `BepInEx/config/BepInEx.cfg` after an update: `UpdateInteropAssemblies = true` gets set to false. With true, BepInEx tries to rebuild the interop files we ship, that fails on this game and the mod doesn't load. The rest of that file stays yours
- array's 6 C1 fix files, only if you already installed C1 yourself and the release has them, see [C1.md](C1.md). It never deletes those

It never writes the mod's settings file. You put Host, Port and JoinKey in `BepInEx\config\com.nightrunners.multiplayer.cfg` yourself, from the NRMP Discord.

**Connects to**
- the NRMP-MOD release on GitHub over https, for the manifest, the release zip and the mod dll, and the NRMP-Launcher release for its own updates: `github.com`, which sends it on to GitHub's download servers `objects.githubusercontent.com` and `release-assets.githubusercontent.com`. Anyone can download those, so it sends nothing about you.
- the server in your own settings file, over plain http, for the player count. It sends a hash of your invite code to find it. No server in there, no player count, and it doesn't connect anywhere else.

**Starts**
- Explorer to show you the logs zip, the game, Steam through `steam://open/main` if it's closed, and the Patreon and Discord pages when you click them

**Doesn't**
- upload logs or crash reports anywhere
- need admin, touch the registry from its own code, run at startup, or install a service

On start it only checks if there's a new release. Nothing downloads until you press UPDATE or INSTALL. Every update has to match a release signed with my key, the public half is built into the exe. That goes for the launcher's own updates too.

It also won't take a release older than the newest one it's seen, so nobody can hand it an old signed one with a bug in it. Every path in a release has to stay inside the game folder, and a download that comes in bigger than the release says gets stopped and thrown away. The server status isn't signed, so only the player count and a short list of plain names get used from it.

## Scanner warnings

Scanner reports can still list things like your computer name, cause .NET and Windows read that stuff while loading a program. The launcher's own code doesn't ask for it.

The exe has no code signing certificate, those cost money, so Windows can still warn about it. Downloads come over https from GitHub. Only the player count is plain http, and nothing from it gets installed.
