# PrimalCraft Mace Receiver 1.9.7

This standalone Gorilla Tag BepInEx 5 plugin receives authenticated developer mace hits and displays their Minecraft effects. The weapon can be equipped and configured only through PrimalCraft's authorized Developer menu. Installing this DLL grants receiver support; it does not grant weapon access.

- **F8** opens receiver settings: incoming-hit consent, particles, sound and update status.
- The standalone plugin has no controller or keyboard equipment shortcut, equipment button, or weapon/enchantment controls.
- Gameplay requires a **private modded standard Infection / Tag room**. Participants must run a compatible authenticated mace runtime. Incoming hits default on and can be disabled in receiver settings.
- With PrimalCraft installed, a shared bridge selects one runtime. The authorized PrimalCraft Developer profile controls the weapon and takes priority. Otherwise the standalone profile controls receiver settings.
- Changing rooms resets weapon equipment. Receiver settings do not equip, hide or modify a developer's weapon.

## Build

Run from the workspace in PowerShell:

```powershell
& '.\mace mod\build.ps1'
```

For another game location:

```powershell
& '.\mace mod\build.ps1' -GamePath 'D:\SteamLibrary\steamapps\common\Gorilla Tag'
```

Output: `mace mod\artifacts\MaceMod.dll`. The build compiles `shared/*.cs` and `src/*.cs` using the installed game's Unity and Photon assemblies, BepInEx 5 and Utilla. It does not copy anything into the game or launch it.

This folder builds within the Totem source workspace. It reuses the public developer-identity verifier, `../src/PixelMeshBuilder.cs`, `../tests/PixelMeshTests.cs`, and the Utilla repair sources/build/tests under `../updater/utilla`, `../compat/utilla` and `../tests`. Keep the checkout together. The standalone plugin contains no developer signing private key and requires no reference to `TotemMod.dll`.

## Automatic updates

Automatic updates require the flat install path `BepInEx/plugins/MaceMod.dll`; nested folders or renamed DLLs show an installation-path message and skip staging.

Automatic updates default on. With the standalone plugin, the first check runs about 10 seconds after startup, followed by checks every six hours. Failed checks retry after five minutes. The F8 receiver window provides an **Automatic Mace updates** switch, status and **Check for updates now** button.

When PrimalCraft 1.9.2 or newer is installed, Totem manages updates for both plugins. The Mace receiver window then shows **Updates managed by PrimalCraft** and directs you to PrimalCraft Settings, so there is one automatic-update switch and no duplicate network check. With older Totem versions or without Totem, Mace manages its own updates.

Downloads and staging run in a background task. The updater accepts signed Mace release manifests and verifies the payload identity, version, hash and supported game build before staging it. Installed plugins remain loaded for the session; a validated companion preloader applies staged updates before plugin loading on the next game launch. DLL-only installs automatically set up that companion helper. No private signing key is included in this plugin.
## Utilla compatibility

The DLL embeds the same validated Utilla compatibility repair used by PrimalCraft. Installing only `MaceMod.dll` is supported: on first launch it adds the missing repair helper to `BepInEx/patchers` and logs that Gorilla Tag must restart. That next launch enables canonical private Modded Infection compatibility with older Utilla. Existing different repair helpers are retained and reported in the log. The standalone ZIP also contains the helper directly so it can be installed before the first launch.

## Local Minecraft assets

`tools/extract-assets.py` reads this user's installed `%APPDATA%\.minecraft` version jars and cached asset objects. It does not download anything. It copies the exact mace item PNG and critical-hit sprite and converts verified local Minecraft smash and wind-charge audio to mono float32 PCM at 44.1 kHz using local `ffmpeg`. Each cached audio object is checked against the asset index SHA-1 before decoding. Original sources and hashes are recorded in `assets/provenance.json`.

The mace is a closed extrusion of the original point-filtered pixel texture, straightened around its handle grip. Visual posing and hit detection use the same grip rotation and head center. There are no cosmetic colliders or cloned rig behaviours. Impacts reuse one mesh batch with at most 64 live critical-hit sparks, at most 16 per impact, and four pooled spatial audio voices.

The developer-controlled enchantment shimmer uses opaque material tint/emission. The original glint and gust PNGs are retained with provenance for future work; the runtime currently renders the original mace and critical-hit sprites. It adds no transparent glint shell.

Minecraft assets belong to Mojang/Microsoft. These extracted copies and DLLs embedding them are for this local personal installation; do not redistribute them as part of a public source or release package.

## Verification

The build runs offline extraction integrity tests, compiled receiver-access/render-safety/allocation checks, solid-extrusion camera/culling/color tests against the actual mace PNG, and protocol and enchantment-mechanics tests. Runtime visual alignment, developer authentication and multiplayer results still need testing in the real headset/game.
