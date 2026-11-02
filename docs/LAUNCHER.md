# What the launcher does

This is the whole list. If it's not on here, the launcher doesn't do it.

**Reads**
- the game folder you pick, and the mod's settings file in it
- `%LOCALAPPDATA%\NightRunnersMP\game-folder.txt`
- whether the game and Steam are running, by process name

**Writes**
- `%LOCALAPPDATA%\NightRunnersMP`: the launcher, `game-folder.txt`, `launcher.new` for a moment during a self update, and the old exe as `.old` until the next start
- a desktop and a Start menu shortcut, after asking on first run
- mod files inside the game folder, listed in `BepInEx/nrmp-files.txt`, and the `[Server]` section of the mod's settings

**Connects to**
- `nrmp-eu-1.duckdns.org` (`128.140.117.115`) over https, for the manifest, files and server status. The only thing it identifies itself with is a hash of the invite code.

**Starts**
- the game, Steam through `steam://open/main` if it's closed, and the Patreon and Discord pages when you click them

**Doesn't**
- need admin, touch the registry from its own code, run at startup, or install a service

On start it only checks if there's a new release. Nothing downloads until you press UPDATE or INSTALL. Every update has to match a release signed with my key, the public half is built into the exe. That goes for the launcher's own updates too.

## Scanner warnings

Scanner reports can still list things like your computer name, cause .NET and Windows read that stuff while loading a program. The launcher's own code doesn't ask for it.

The exe has no code signing certificate, those cost money, so Windows can still warn about it. Downloads use https on nrmp-eu-1.duckdns.org. Plain http on the IP only stays up for launchers older than that change.
