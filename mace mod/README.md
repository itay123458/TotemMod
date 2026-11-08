# Mace removed ? 1.9.8

The mace feature has been retired. This build produces a small inactive `MaceMod.dll` with the original plugin ID so existing automatic updaters can replace older receivers safely.

It has no hit receiver, physics, models, particles, menu or update loop. PrimalCraft retains the totem features.

Build with `./mace mod/build.ps1`. The output is `mace mod/artifacts/MaceMod.dll`. The build compiles only `src/MacePlugin.cs`; previous gameplay sources are not compiled.

You can delete `BepInEx/plugins/MaceMod.dll` while Gorilla Tag is closed. Keep the updater helpers used by PrimalCraft.
