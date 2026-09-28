# VR browser player

Open the MVC player in the headset's WebXR browser using a **trusted HTTPS**
address, add videos/assets, then use the headset icon in the player toolbar.
The same native video, playlist, playback events and EDI synchronization continue
to own playback. Videos remain local to the browser where they were selected;
this mode does not transfer a desktop browser's File objects to another headset.
Supported codecs/resolutions depend on that browser and headset.

## Controls

| Input | Action |
| --- | --- |
| Grip / squeeze, pointing at the video | Hold to move the flat video screen inside its forward viewing cone; it stays facing the viewer |
| Trigger on a control | Activate the original HTML control; drag sliders, choose select options and edit numeric fields |
| Trigger held on the video | Also moves/rotates it using the pointer pose |
| Short trigger on video or empty space | Show/hide the complete fullscreen-style control overlay |
| A (X on left Touch controller) | Original video left-mouse sequence: video play/pause, or devices pause/resume when the player stroker option is enabled |
| B (Y on left Touch controller) | Original right-mousedown variant action, respecting whether that feature is enabled |
| Stick up/down | Intensity, respecting the existing intensity toggle and per-device participation |
| Stick while pointing at video controls | Seek, horizontally or vertically; the volume area takes priority |
| Stick while pointing at volume | Volume up/down |

The lock button enables delayed head following. It captures the video's chosen
position relative to the current head pose. After two seconds
of stillness by default, a movement over 8 degrees or 10 cm eases back into that
view position. Small motion is ignored. Dragging suspends following and records
the new position on release. The menu adjusts the delay (0.5–5 s), smoothing,
screen width/distance and horizontal/vertical offsets. Recenter restores the screen
in the current view. Native session visibility loss pauses playback through the
same media event/synchronization path as desktop pause.

## Video formats

The VR surface is always a flat screen. Filename matching is case insensitive and
only detects mono or side-by-side packing:

| Markers | Interpretation |
| --- | --- |
| `LR`, `SBS`, `3DH`, `3DPH` | Left/right images |
| `RL` | Right/left images |
| `HSBS` | Half-size side-by-side packing, restoring the full screen aspect |

For example, both `scene_180_LR.mp4` and `movie_SBS.mp4` open as flat SBS screens;
the `180` marker is deliberately ignored. Unmarked files open flat/mono.
Packing, eye order and half-size overrides persist **per filename** under
`edi-player-vr`; view/follow settings persist separately in the same key.
"Detect filename" clears only that video's override.

## Ownership and extensions

`immersive.mjs` owns entry, exit, preferences and the module's callback ports.
`runtime.mjs` owns the WebXR session renderer, poses, controller subscriptions,
grab/follow state and frame loop. `video-surface.mjs` owns the video texture and
per-eye geometries. `format.mjs` and `motion.mjs` are hardware-free math/input seams.
`menu.mjs` owns only the additional VR settings UI.

`html-panels.mjs` temporarily moves the **original** fullscreen toolbar, video
controls, playlist and device panels into one overlay surface on the video. It
paints that HTML to one canvas texture and translates ray coordinates back to
the original events. It observes DOM mutations, scroll and input changes; no
second player or device-menu implementation exists. Native selects get a popup backed
by the original options and change handler. Existing open dialogs are captured
in place; range handles and playlist/device reordering retain their handlers.
Exiting restores all moved nodes using placeholders, disconnects observers and
controller listeners, cancels the frame loop and disposes geometries/textures.
Loading or painting failure returns to desktop controls with a visible message.

The player composition root imports only the lightweight entry module. Three.js
and the HTML renderer load when entering VR, from pinned local files under
`wwwroot/lib/vr`; normal playback does not need a CDN. No EDI API or core/device
code is changed by this mode.

Quest rendering uses Three.js' WebXR framebuffer scaling and compositor foveation,
with MSAA disabled. HTML snapshots use one shared, throttled capture budget and
CSS-pixel textures so DOM changes cannot continuously stall the XR frame loop.

## Quest diagnostics

While a session is running, `window.ediVrDiagnostics` contains the current FPS,
last and worst JavaScript frame time, video readiness, HTML snapshot duration and
the last runtime error. The last sample remains available after leaving VR. A
frame-loop exception is also printed as `VR frame failed` and ends the session
with the error shown in the VR settings status instead of leaving a silent black
screen.

For a physical headset, enable Developer Mode and USB debugging, connect it with
ADB, open the player in Meta Quest Browser, then inspect that tab from desktop
Chrome at `chrome://inspect/#devices`. In DevTools, inspect
`window.ediVrDiagnostics` in Console and record the Performance panel while
showing and hiding the menus. Compare `fps`/`maxFrameMs` with the panel
`lastCaptureMs` and `maxCaptureMs`: a frame spike matching a capture identifies
main-thread HTML rasterization; low FPS without capture spikes points instead to
video decode, GPU fill rate or texture upload.

## Verification

Run `node --test Edi.Mvc/Tests/*.test.mjs` from the solution root. With Playwright
available and Edge installed, run `node --test Edi.Mvc/Tests/browser/*.test.mjs`.
The browser tests serve static files and mock all EDI/device endpoints. The fake
WebXR boundary exercises the actual Three.js/WebGL rendering, A/B configuration,
stick routing, trigger/grip dragging for SBS and mono, hand rotation, delayed head
following with micro-motion tolerance, reentry and error cleanup.
HTML panel PNGs can be inspected at `%TEMP%/edi-vr-settings-qa.png`.
Physical headset ergonomics, decoding limits and controller compatibility still
need a headset test; the simulator does not establish those.

Reference APIs: [WebXR security](https://developer.mozilla.org/en-US/docs/Web/API/WebXR_Device_API/Permissions_and_security),
[WebXR gamepad mapping](https://www.w3.org/TR/webxr-gamepads-module-1/),
[Three.js WebXRManager](https://threejs.org/docs/pages/WebXRManager.html),
[DeoVR format documentation](https://deovr.com/documentation).
