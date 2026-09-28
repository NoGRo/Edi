# Browser player architecture

`../edi-player.js` is the composition root. It creates one session, wires explicit
callback dependencies, then mounts event handlers after all controllers exist.
Existing preference keys are retained; video pairs and device order have additional browser-local keys.

| Module | Responsibility |
| --- | --- |
| `synchronization.mjs` | EDI command queue, gallery seek, stroker pause/resume and resynchronization |
| `html-media-adapter.mjs` | Native video implementation of the playback port |
| `playback-events.mjs` | Media event transitions, buffering, end, looping and autoplay |
| `playlist.mjs` | Local video lifecycle, ordering, selection, object URLs and playlist rendering |
| `assets.mjs` | Shared asset library, uploads, downloads, merge/filter rules and server upload memory |
| `asset-storage.mjs` | IndexedDB persistence, retaining database `edi-player`, version 2 |
| `asset-workflow.mjs` | Stop/reload/resync coordination around asset changes |
| `preferences.mjs`, `positions.mjs` | Session initialization, persisted settings and resume positions |
| `controls.mjs`, `options.mjs` | Video controls and playback option buttons |
| `device-routing.mjs` | Per-device participation, transient pause variants and scaled ranges |
| `video-variants.mjs` | Active-video asset filtering and history/continuity/default selection |
| `fullscreen.mjs`, `overlays.mjs`, `input.mjs` | Desktop presentation, pointer/keyboard input and overlays |
| `vr/` | Immersive WebXR video, controller input, delayed head follow and original HTML panels as 3D surfaces |
| `edi-api.mjs`, `media-files.mjs`, `elements.mjs` | HTTP boundary, filename rules and DOM lookup |

## Playback integrations

Synchronization and playback events consume `media`, which supplies:

- `currentTime` in seconds (read/write), `duration`, `paused`, `ended`, `seeking`;
- `readyState` with HTML media readiness semantics (1 = metadata, 3 = future data);
- `play()` returning a Promise, `pause()`, `setSource(item)`, `clearSource()`;
- `addEventListener` and `removeEventListener` with standard media event names.

An external player can implement this port and reuse synchronization without a
DOM video. Event notifications must follow acknowledged remote state, so seeking,
buffering and pause continue to stop or resynchronize EDI correctly. The desktop
controls currently operate on the native video and require their own presentation
adapter when used with an external player.

The [VR module](vr/README.md) renders the same video as a movable flat WebXR
screen, with separate eye regions for SBS video. It reuses the session, asset
manager and synchronization. The original fullscreen controls are painted as one
interactive overlay on that screen, so their future changes also reach the headset.

## Asset ownership

Both the player and variant generator import the same `assets` instance.
The persistent library holds source files across page reloads. The in-memory cache
holds files available to variant generation. `getUploaded()` reports files confirmed
by successful uploads or server recovery in this session; restoring IndexedDB does
not imply those files are still loaded on the server.

Merging is case insensitive. Playlist filtering retains matching scripts/audio,
variants/axes, definitions and bundles. `Definitions_auto.csv` is excluded from
persistence. POST replaces the entire server set, PUT sends only generated files,
and DELETE clears assets. Repeated POST requests still reach the server because they
also stop playback and reload repositories. Upload memory never skips that behavior.

Downloads are shared across concurrent requests, persistence writes are serialized,
and old downloads cannot overwrite a newer cache. Existing `edi-assets-cached`,
`edi-assets-persist-requested`, `edi-assets-reloaded` and device refresh notifications
remain available to other UI components.

## Verification

With multiple devices, the three buttons beside each device name select participation
in pause/resume, intensity and variant switching. Choices persist in browser storage;
a single device uses the original global controls. Blue indicates participation,
gray exclusion and red a stopped state. A selective pause keeps the visible variant
and pair intact while applying `None` to the server; resume restores the latest
selected variant and lets EDI synchronize it to its running timeline.
Both variant selectors remain editable when participation is disabled. Edits are
saved without device commands; enabling participation applies the choice for the
current global side.

All participants use global pause/resume and intensity. Selective variants use one
`POST /Devices/Variants?persist=false` batch; selective ranges use parallel
`POST /Devices/{name}/Range/{min}-{max}?persist=false` requests, scaled from
`baseMin`/`baseMax` in the devices response. These requests do not save configuration.
Variant property notifications handle stop/resynchronization without an explicit
stop before the assignment. Pending intensity values are replaced by the newest
value, and unchanged selective variants/ranges are skipped. Asset preparation reuses
the shared cache and confirms or uploads missing files before a selection becomes active.

Device rows reorder vertically using the playlist's arrow handle and faded drag feedback.
The browser retains that order across refreshes and reconnects. The button between a
device's selectors swaps its primary/secondary choices (including their automatic bases)
and applies only that device's active side; the global side remains unchanged.

Video options are derived from matching assets through `funscript-tools`, intersected
with the device's advertised variants. History is stored per video filename and device.
Without history, compatible previous choices continue; otherwise a valid configured
default or the first available original is used. An explicit `Stopped` survives navigation.
Files dropped together upload before initial variant preparation, so the returned
definitions update every playlist duration immediately. The local media source loads
before device preparation; device Play commands and autoplay await variant readiness.
Native metadata supplies a duration when a video has no matching EDI definition.

`assets.prepareVariant` owns incremental upload and automatic generation. It reuses
confirmed server filenames, caches preparation results, and fingerprints source files
to regenerate derivatives after an original changes. Automatic names include their
original base so different devices' choices do not overwrite one another. Multiple
original bases open a choice dialog; the choice is remembered with the video/device pair.
Automatic variants never appear as possible bases. Selecting or restoring an automatic
choice prepares and uploads it before activation, including the inactive side.

From the repository root:

```powershell
node --test Edi.Mvc/Tests/*.test.mjs
```

An optional browser suite uses Playwright and installed Microsoft Edge:

```powershell
node --test Edi.Mvc/Tests/browser/player.test.mjs
```

Playwright must be available in Node's module resolution (or through `NODE_PATH`).
The suite serves static files and mocks every EDI/device API call; it never starts
an EDI host or connects to hardware. A small real WebM fixture verifies file drops,
every playlist duration and browser playback. Unit tests also cover playback
transitions with a media port fake.
