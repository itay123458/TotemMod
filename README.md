# Totem Mod

Version **1.9.2** for both Totem Mod and Mace Mod, for the installed Gorilla Tag build, BepInEx and Utilla.

[Download the latest release](https://github.com/itay123458/TotemMod/releases/latest) · [Source](https://github.com/itay123458/TotemMod)

## Install and controls

Close Gorilla Tag, download the release ZIP and extract it into the Gorilla Tag folder. It installs `BepInEx/plugins/TotemMod.dll` plus `TotemMod.Updater.dll`, `TotemMod.CompanionUpdater.dll` and `TotemMod.UtillaFix.dll` under `BepInEx/patchers`. The separate Mace ZIP installs MaceMod.dll and its required patchers. BepInEx and Utilla are required separately. Keep one copy of each plugin. Restart after replacing DLLs and create a new private Modded Infection room. Source builders get the Totem DLLs in `artifacts/` and MaceMod.dll under `mace mod/artifacts/`.

- **Y:** equip or unequip. Unequipping spends nothing. A pop spends one totem; release and press again for another.
- **Hold X** (default 0.6 seconds), or **F7:** open/close settings.
- Aim the right controller and pull the trigger. Hold the trigger to drag sliders.
- Category tabs select a category. The arrows beside the page title move between its pages.
- Keyboard arrows/Enter select controls; **Tab** changes page; **Shift+Tab** or **Page Up / Page Down** changes category; Escape closes settings.

The default supply is **5**, regenerating **1 every 40 seconds**. Additional pops preserve recharge progress. All gameplay, previews and effects require a modded room.

The controller pointer uses one filtered position for the cursor, laser, hover and click. Small hand movements are smoothed; larger movements respond immediately. The thin tapered laser ends at a hollow square cursor with hover/drag colors and a short click pulse. Sliders stay captured until the trigger is released, including after a brief loss of aim. Smoothing can be disabled under Interface / Menu and Pointer.

Choose **Effects / Sound and Motion / Animation: Minimal** for a quick compact pop: 0.78 seconds at normal speed, a smaller icon with a gentle settle, and up to twelve smaller sparks that fade with it. The **Minimal Pop** effect preset selects this animation too. New configurations default to Minimal; existing animation choices are retained. Minecraft Sway, Spin and Still remain available.

## Organized categories

The developer has six top-level categories and 26 named pages. Approved Beta accounts have five categories; other accounts have four. Page selection is remembered separately for each category while the menu exists.

| Category | Pages and controls |
|---|---|
| Game | Presets, Rules, Round: five presets, stock/recharge/cap sliders, protection, auto-start, timer controls, refills and time-cap shortcuts. |
| Totem | Size and Grip, Palm Position, Palm Rotation: global/held scales, grip presets, saved custom grip, position/rotation sliders and 90-degree steps. |
| Effects | Particles, Sound and Motion, Friends and Quality, Effect Presets: amount/size/spread/colors/shape/brightness/spin; sound volume/pitch; animation style/speed/distance/height; friend range/render scale/sounds; haptics; six local effect presets and preview. |
| Interface | Menu and Pointer, HUD, HUD Position, Notices and Controls, Updates: pointer/HUD preferences, local controls, resets, automatic updates, interval, check now, status and pending-update cancellation. |
| Beta | Totem Model, Tint and Motion, Model Orientation, Experiments: selectable player avatar, crystal/block/star, tint/rainbow, scale, spin, bob, rotation, pop appearance, snapshot refresh and experimental presets. |
| Dev | Mace and Enchantments, Mace Effects, Beta Access, Local Tools, Room Control, Practice Shortcuts, Reset and Diagnostics: all developer tools are inside this single account-only category. |

Minecraft's actual menu textures and click come from the local installation. Sliders show pending developer room changes while awaiting the host. Intermediate slider values coalesce; pending actions are canceled on host or room changes.

## Beta appearance

Go to **Beta / Totem Model**, enable Experimental Appearance, choose **Player model**, then use **Player** to select your own avatar or another player in the current room. **Refresh player snapshot** captures a fresh pose and appearance.

Avatar models are render-only snapshots. The code bakes the existing body/face and one supported cosmetic mesh, copies their render data, and normalizes the miniature's size. It does not clone a VRRig, activate a source player, add player networking or physics, or move another player. Capture is limited to three renderers and 30,000 total vertices. Generated totems are excluded so a miniature cannot recursively capture an older miniature. Up to three nearby remote avatar totems render within 12 meters; other remote avatar totems use the classic model.

The pose is a snapshot rather than a continuously animated avatar. Missing avatar data uses the classic totem and retries while equipped. Selection resets to yourself when changing rooms or when the selected player leaves.

Beta appearance is **shared with players using 1.8.0 or newer** in private modded rooms. Model selection, tint, scale, tilt, spin, bob, rainbow and pop appearance synchronize with changed settings at most twice a second. Each client renders its own snapshot; mesh data and animation frames are not streamed. Older versions see the classic totem. Gameplay stock, tag protection and shared timers are unchanged by Beta visuals.

The Beta category and outgoing Beta appearance require the developer account, an approved permanent Gorilla Tag account, or a signed room grant. The chosen friend's account is included without publishing its raw Player ID. All updated mod users can see authorized Beta appearances, even if their own Beta menu is locked. Under **Dev / Beta Access**, select a player to grant or revoke access for this room. Temporary grants end when the developer leaves; a room revocation also overrides permanent approval until that session ends. Account checks use authenticated IDs, never nicknames or custom identity properties.

Other experiments include recoloring the classic totem, crystal/block/star shapes, original player colors or RGB tint, rainbow speed, held spin/bob, model orientation, and using the chosen model during your pop. Mini Me, Emerald Crystal, Rainbow Star and Golden Block presets are included. Disable Beta or choose Return to Classic to undo the experiments.

## Minecraft mace

The separate receiver mod lives in `mace mod/` and builds to `mace mod/artifacts/MaceMod.dll`. It needs BepInEx and Utilla; it does not require Totem Mod to receive hits. **F8** opens incoming-hit, particle and sound preferences. **Only the developer can equip a mace, and only from Totem's Dev / Mace and Enchantments page.** B, M and desktop equip controls are removed. The developer's authenticated Steam account and private signing key are required; editing a config or becoming room master does not unlock the weapon.

Mace hits work only in **private modded standard Infection / Tag**, between users running Mace Mod 1.0.1 or Totem Mod 1.9.1 or newer. The new protocol rejects unsigned equipped states and binds the developer's proof to their actual account, room, actor, session, state and revision. Old unrestricted mace clients cannot attack updated receivers. Receiving hits can be disabled in the active mod's settings. When both mods are installed, one shared runtime handles peer messages, effects and physics. The developer's Totem settings take priority; other accounts use standalone receiver settings when installed.

Swing the mace head into a participating player. Falling distance determines smash strength: one block equals one meter at normal player scale. Grounding, new ascent, teleports, stale samples and completed hits clear old fall credit. Hit geometry, recent swing history, range, walls, current opt-in, fresh sessions and message order are checked again by the recipient.

An **untagged attacker knocks the target back**. The target's own mod applies bounded local velocity. A **tagged attacker requests a native tag** unless the target has an equipped, charged totem and the current Totem rules permit protection. A save spends one charge after the acknowledged hit is published and plays the normal totem pop. The developer can use the mace as a guest: after a validated, acknowledged strike, the local attacker uses the game's ordinary `GameMode.ReportTag` path. The host applies native tag validation and cooldown through normal synchronization; the host does not need Mace Mod for this path. No host takeover is used.

**Density 0–V** increases falling-hit strength, **Knockback 0–II** adds shove strength, and **Wind Burst 0–III** gives the attacker an upward rebound after a smash of at least 1.5 blocks. These are Gorilla Tag adaptations; there is no Minecraft armor system, so Breach is not included. Size, power, impact particles, sounds and opaque enchantment shimmer are configurable. The mace texture, critical-hit pixels and hit/smash/wind audio come from the local Minecraft installation. Enchantment reference: [Minecraft's Tricky Trials release notes](https://feedback.minecraft.net/hc/en-us/articles/27547857163917-Minecraft-Java-Edition-1-21-Tricky-Trials).

## Totem protection

In a private modded standard Infection/Tag room, an updated host can spend an equipped player's totem instead of applying one accepted tag. The host's ordinary synchronized infection state keeps that player untagged for everyone. Native tag validation stays intact. Public, competitive and unsupported modes have cosmetic pops only.

Friends running the updated visual mod see equipped totems and hear nearby Minecraft pop sounds. Late joins receive the current held state and host rules without replaying old pops or stock refills.

| Preset | Stock | Recharge | Timer |
|---|---:|---:|---|
| Default | 5 | 40 seconds | Manual, 3:00 |
| Scrims | 3 | 60 seconds | First confirmed tag, 3:00 cap |
| Filming | 5 | 10 seconds | Manual, 3:00 |
| Casual | 8 | 30 seconds | Manual, 3:00 |
| Sandbox | 20 | 1 second | Manual, 3:00 |

Starting a configured **3:00 timer selects the full Scrims preset**, then starts its countdown. Other timer lengths preserve their configuration. The cap pauses totem protection until the timer is reset or restarted; it does not reset the native game round. Presets preserve stock and proportional recharge progress; refill is a separate action.

Only the developer's logged-in Steam account sees **Dev**. A nickname, host role or editable config cannot unlock it. Each developer action rechecks identity, and a changed login discards the old menu.

The private host applies shared rules. Developer guest controls are always enabled when the host runs **1.6.1 or newer**; there is no host enable switch. Commands require the developer's signature plus valid membership, host session, timing and replay order. The developer's private key stays on this computer in `BepInEx/config/TotemMod.developer-key.xml`, with a backup under `%USERPROFILE%/.totemmod/`. Friends receive the DLL only. Other hosts need no private key.

Developer room actions never take host authority or bypass native tag validation. An unsupported host, missing key or request timeout reports unavailable. Local test pops spend no stock and change no infection or friend state.

## Utilla Infection compatibility

Old Utilla advertises `MODDED_Infection`; vanilla's manager validation requires exact `Infection`. This mismatch can prevent non-Utilla clients from recognizing the manager.

The private-room patch advertises native Infection and keeps explicit modded opt-in in `org.legoandmars.utilla.mode = MODDED_Infection`. Patched Utilla projects the modded designation locally for its callbacks. Serializer and tag validation remain intact.

The included preloader repairs the supported old Utilla automatically before plugins load. It creates an exact backup outside the plugins directory under `BepInEx/cache/TotemModUtilla/backups/`, then atomically replaces the DLL with the compatibility fix. It also finds nested Utilla installs. Already fixed copies stay unchanged. Unknown versions, duplicate Utilla plugins, locked files and a different game build are retained with a log message. Nothing deletes the old DLL before a replacement is ready. Utilla settings remain intact.

When receiving only TotemMod.dll through the automatic updater, its first launch installs the repair helper. **Restart once more** to apply the Utilla repair. Extracting the full ZIP installs the helper immediately. No additional download or redistributed Utilla binary is required: the helper patches the verified installed original using the included compatibility code.

The creator/host needs patched Utilla and a **new private Modded Infection room**. Vanilla friends need no plugin. Utilla mod users need patched Utilla for modded callbacks; an older Utilla sees ordinary Infection. Ordinary Infection clears the marker. The repair targets the tested game/Utilla hashes; another game build requires checking compatibility again.

## Automatic updates

Automatic updates are on by default. Totem checks its own release and the installed standalone Mace DLL from the same public GitHub release feed shortly after startup, then every six hours by default. Failed checks retry after five minutes. Configure both under **Interface / Updates**, or check manually there. No GitHub login is needed to receive updates. Mace is updated only if installed; Totem does not add the standalone plugin when it is absent.

Mace-only installs have their own startup/six-hour automatic check and F8 update controls. When Totem 1.9.2 or newer is installed, Totem manages both products and Mace skips duplicate checks. Both release versions now match. Each DLL uses a separate signed manifest, fixed asset name, plugin identity and signature domain, so one product cannot be substituted for the other.

New versions must have a valid publisher RSA signature, exact DLL hash/size, expected plugin identity/version and matching tested game build. Downloads are bounded and HTTPS-only. Running plugins stage verified files without replacing loaded DLLs. At the next game launch, the shared companion preloader checks each queue again and atomically installs updates before either plugin loads. Previous DLLs remain outside plugins in `BepInEx/cache/TotemModUpdater/backups/` and `BepInEx/cache/MaceModUpdater/backups/`. A failed product update retains that product's old DLL and does not stop the other queue from being checked.

The first DLL-only upgrade to 1.9.2 installs the companion helper automatically. Restart once more after the update-ready notice to activate it and apply a waiting Mace update. The full ZIP includes it immediately. Existing helper files with unexpected bytes are retained and reported for replacement while the game is closed.

Network failures, bad signatures, corrupt files, downgrades, unsupported helper versions and game mismatches retain the installed plugin. Old pending files for an incompatible game are cleared on the next check so a compatible release can be discovered. The signed updater changes Totem and installed Mace only; the separate repair helper handles the supported Utilla fix described above. Neither changes game assemblies, personal settings or developer keys. Future supported releases of both mods update automatically.

To publish a new version as the repository owner, change `ModVersion` in `src/AutoUpdateSettings.cs`, update the README and run `tools/publish-release.ps1 -Publish`. It builds/tests, signs the manifest locally, packages an explicit source allowlist, preserves repository history and publishes a new immutable version tag. The update-signing key stays under `%USERPROFILE%/.totemmod/update-signing-private.xml`; it is separate from the developer-command key and must remain private. Neither key is in GitHub or the release ZIP.

## Build and validation

Run `./build.ps1`. It uses the existing Windows C# compiler and installed game references. Missing Minecraft assets are extracted locally by the scripts in `tools/` (Python and ffmpeg); no SDK or loader is installed.

Checks cover equip/inventory, palm placement, accepted tags and protection, private signed commands, host migration/replay/membership, Scrims selection, developer identity, remote slider intent, configurable X hold timing, category paging, shared Beta authorization/replay, closed mesh geometry, batched particles and render-only avatar safety. Updater tests cover strict signed manifests, bounded downloads, game/version checks, tampering, locks, junctions, staging, atomic installation and exact backup bytes. Public source builds use a temporary test-only signing identity when no publisher key exists; production DLLs always retain the fixed trusted public key. `build.ps1 -UseTestUpdateKey` explicitly tests that public-build path.

The Utilla build script is `compat/utilla/build.ps1`; its checks include the actual installed game's mode-name predicate. The predicate rejects the legacy alias and accepts the canonical name.

These are compilation, assembly, mathematical and boundary-fixture checks. The newest category layout and Beta avatar appearance need headset verification; mixed-client Infection and developer guest commands need live multiplayer verification. Earlier classic mesh, animation and audio were checked in game.

The menu remains inactive while closed, labels refresh at 10 Hz, config writes debounce for one second, and friend effects are distance-culled. Each pop uses one particle renderer for up to 64 sparks. The classic mesh has 304 vertices and 152 triangles, down from 1,240 and 620, while preserving its pixel silhouette and colors. Solid depth-writing shaders prevent tinted totems showing their internal faces; mirrored avatar meshes correct their triangle winding. Avatar snapshots use a four-source cache with ten-second idle expiry and a shared capture throttle. Changing tint, spin or scale reuses the existing snapshot. HUD transforms, hands and identity checks are cached; owned meshes, materials and textures are released with their render instances. No measured frame-rate claim is made.

## Backups and provenance

Timestamped previous DLLs are in `backups/`. Original Utilla is also in `compat/utilla/backups/Utilla.original.dll`. Private recovered source remains under `analysis/original/`; replacement source is in `src/`.

Minecraft assets belong to Mojang/Microsoft. Local extractors verify cached sounds and record provenance under `assets/`. Source is available under GPL v2; Minecraft asset ownership is separate. Original game assemblies, recovered original mod source, local logs, personal configurations and signing keys are excluded from the public repository and release packages.

Notification template/fonts: [GorillaNotifications](https://github.com/hamburbur-org/Gorilla-Notifications). GPL v2 license and attribution are in `third_party/GorillaNotifications/`; adapted code is `src/TotemNotice.cs`. The Utilla binary is a private local compatibility modification.
