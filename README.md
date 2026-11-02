<div align="center">

# Night-Runners Multiplayer Launcher

Installs and updates the Night-Runners Multiplayer mod, that's it.

<a href="https://discord.gg/ERk9SxD4Ec"><img src="https://img.shields.io/badge/Discord-Join%20for%20support-5865F2?style=for-the-badge&logo=discord&logoColor=white" alt="Join the Discord for support"></a>
<a href="../../releases"><img src="https://img.shields.io/badge/Download-Releases-E0002B?style=for-the-badge&logo=github&logoColor=white" alt="Download from Releases"></a>
<a href="docs/LAUNCHER.md"><img src="https://img.shields.io/badge/What%20it%20touches-LAUNCHER.md-30363D?style=for-the-badge&logo=readthedocs&logoColor=white" alt="What the launcher touches"></a>

<img src="https://img.shields.io/badge/platform-Windows-0078D6?style=flat-square&logo=windows&logoColor=white" alt="Windows">
<img src="https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?style=flat-square&logo=dotnet&logoColor=white" alt=".NET Framework 4.8">
<img src="https://img.shields.io/badge/admin-not%20needed-2EA043?style=flat-square" alt="No admin needed">

</div>

NIGHT-RUNNERS is made by PlanetJem, this is a fan project by MILLIYEN.

It's one small exe. No admin rights, doesn't touch the registry from its own code, doesn't run at startup and doesn't install a service. On start it only checks if there's a new release. Nothing downloads until you press UPDATE or INSTALL, and every update has to match a release signed with my key.

Everything it reads, writes and connects to is in [docs/LAUNCHER.md](docs/LAUNCHER.md). Source is right here so go check.

## Download

Grab the zip from Releases and check it against `SHA256SUMS.txt`.

The exe isn't code signed, so Windows SmartScreen or Defender can warn about it. Don't take my word for it, you can build it yourself.

## Install

1. Unzip and run `NightRunnersLauncher.exe`. If Windows warns you, click **More info**, then **Run anyway**.
2. Pick your Night-Runners game folder.
3. Paste your invite code when it asks. No code yet? Ask in the Discord.
4. Press **INSTALL**, then **PLAY**.

When there's a new release the launcher tells you. Press **UPDATE** and that's it.

## Build it yourself

You need the .NET SDK. The launcher targets .NET Framework 4.8, which comes with Windows.

```
dotnet build src -c Release
```

The exe ends up in `src\bin\Release\net48`. With the same tools you should get the same hash as the release. If yours is different, the launcher offers to replace itself with the published one.

## What's not in here

No join key, no signing key and no server code. The join key comes in with an invite (FOR NOW!). 

## Support

Stuck, found a bug, or need an invite code? Come say hi.

<a href="https://discord.gg/ERk9SxD4Ec"><img src="https://img.shields.io/badge/Discord-NRMP%20%7C%20Night--Runners%20Multiplayer-5865F2?style=for-the-badge&logo=discord&logoColor=white" alt="NRMP Discord"></a>

Bring your `BepInEx\LogOutput.log` if something broke, it saves a lot of guessing.

## Credits

NIGHT-RUNNERS by PlanetJem. Launcher by MILLIYEN. BY MODDERS FOR MODDERS.
