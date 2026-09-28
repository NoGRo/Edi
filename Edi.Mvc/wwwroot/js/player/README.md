# Browser player architecture

`../edi-player.js` is the composition root. It creates one session, wires explicit
callback dependencies, then mounts event handlers after all controllers exist.
The UI and persisted preference keys are unchanged.

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
| `fullscreen.mjs`, `overlays.mjs`, `input.mjs` | Desktop presentation, pointer/keyboard input and overlays |
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

A future Quest interface can render a video texture in WebXR, interpreting SBS eye
regions in its own renderer. It can reuse the media port, session, asset manager and
synchronization while replacing the desktop controls, fullscreen and input modules.
This refactor introduces the boundary; it does not implement WebXR or external players.

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
an EDI host or connects to hardware. Its video fixture tests UI/file lifecycle,
while playback transitions are tested with a media port fake in the unit suite.
