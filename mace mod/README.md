# Minecraft Mace Mod

A separate Gorilla Tag BepInEx 5 plugin built in this folder. The same mace runtime is also compiled into Totem Mod. When both are installed, a shared bridge elects one runtime, so there is one held mace, one hit pipeline and one effect stream.

- **B** on the right controller toggles the mace once per press. **M** does the same when keyboard control is enabled.
- **F8** opens the standalone settings window. Opening a settings menu pauses outgoing mace hits and hides the held mace.
- With Totem Mod installed, its **Developer** settings page also exposes mace settings. The Totem Developer page takes priority while its authorized Developer client is active; otherwise the standalone settings apply.
- Gameplay requires a **private modded standard Infection / Tag room**. Everyone receiving mace effects must run a compatible mace runtime and opt in. The selected settings profile controls incoming-hit consent. Equipping resets on room changes.
- Enchantments: Density 0�5, Knockback 0�2 and Wind Burst 0�3. Size 0.5�1.5�; power 0.25�2�. Incoming hits, particles, audio and opaque enchantment shimmer have separate switches.

## Build

Run from the workspace in PowerShell:

```powershell
& '.\mace mod\build.ps1'
```

For another game location:

```powershell
& '.\mace mod\build.ps1' -GamePath 'D:\SteamLibrary\steamapps\common\Gorilla Tag'
```

Output: `mace mod\artifacts\MaceMod.dll`. The build compiles all `shared/*.cs` and `src/*.cs` using the installed game's Unity and Photon assemblies, BepInEx 5 and Utilla. It does not copy anything into the game or launch it.

This folder is independently buildable within this Totem workspace. It reuses `../src/PixelMeshBuilder.cs`, `../tests/PixelMeshTests.cs` and the workspace Utilla repair sources/build/tests under `../updater/utilla`, `../compat/utilla` and `../tests`. Keep this source checkout together. No reference to `TotemMod.dll` is required.

## Utilla compatibility

The DLL embeds the same validated Utilla compatibility repair used by Totem Mod. Installing only `MaceMod.dll` is supported: on first launch it adds the missing repair helper to `BepInEx/patchers` and logs that Gorilla Tag must restart. That next launch enables canonical private Modded Infection compatibility with older Utilla. Existing different repair helpers are retained and reported in the log. The standalone ZIP also contains the helper directly so it can be installed before the first launch.

## Local Minecraft assets

`tools/extract-assets.py` reads this user's installed `%APPDATA%\.minecraft` version jars and cached asset objects. It does not download anything. It copies the exact mace item PNG and critical-hit sprite and converts verified local Minecraft smash and wind-charge audio to mono float32 PCM at 44.1 kHz using local `ffmpeg`. Each cached audio object is checked against the asset index SHA-1 before decoding. Original sources and hashes are recorded in `assets/provenance.json`.

The mace is a closed extrusion of the original point-filtered pixel texture, straightened around its handle grip. Visual posing and hit detection use the same grip rotation and head center. There are no cosmetic colliders or cloned rig behaviours. Impacts reuse one mesh batch with at most 64 live critical-hit sparks, at most 16 per impact, and four pooled spatial audio voices.

The optional enchantment shimmer uses opaque material tint/emission. The original glint and gust PNGs are retained with provenance for future work; the runtime currently renders the original mace and critical-hit sprites. It adds no transparent glint shell.

Minecraft assets belong to Mojang/Microsoft. These extracted copies and DLLs embedding them are for this local personal installation; do not redistribute them as part of a public source or release package.

## Verification

The build runs offline extraction integrity tests, compiled render-safety and allocation checks, the shared solid-extrusion camera/culling/color tests against the actual mace PNG, and the protocol and enchantment-mechanics tests in `tests/`. Runtime visual alignment, controller behavior and multiplayer results still need testing in the real headset/game.
