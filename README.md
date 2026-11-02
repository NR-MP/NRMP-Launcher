# Night-Runners MultiPlayer Launcher

Installs and updates the Night-Runners MultiPlayer mod, that's it. NIGHT-RUNNERS is made by PlanetJem, this is a fan project by MILLIYEN.

It's one small exe. No admin rights, doesn't touch the registry from its own code, doesn't run at startup and doesn't install a service. On start it only checks if there's a new release. Nothing downloads until you press UPDATE or INSTALL, and every update has to match a release signed with my key.

Everything it reads, writes and connects to is in [docs/LAUNCHER.md](docs/LAUNCHER.md). Source is right here so go check.

## Download

Grab the zip from Releases and check it against `SHA256SUMS.txt`.

The exe isn't code signed, so Windows SmartScreen or Defender can warn about it. Don't take my word for it, you can build it yourself.

## Build it yourself

You need the .NET SDK. The launcher targets .NET Framework 4.8, which comes with Windows.

```
dotnet build src -c Release
```

The exe ends up in `src\bin\Release\net48`. With the same tools you should get the same hash as the release. If yours is different, the launcher offers to replace itself with the published one.

## What's not in here

No join key, no signing key and no server code. The join key comes in with an invite (FOR NOW!). 

## Credits

NIGHT-RUNNERS by PlanetJem. Launcher by MILLIYEN. BY MODDERS FOR MODDERS.
