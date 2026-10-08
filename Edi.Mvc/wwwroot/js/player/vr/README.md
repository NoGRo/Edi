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
| Trigger held on the video | Also moves/rotates it using the pointer pose |
| Point near the button below/right of the video | Reveal the lightweight Three.js `Exit VR` button |
| Trigger on `Exit VR` | End the immersive session and return to the unchanged web player |
| A (X on left Touch controller) | Original video left-mouse sequence: video play/pause, or devices pause/resume when the player stroker option is enabled |
| B (Y on left Touch controller) | Original right-mousedown variant action, respecting whether that feature is enabled |
| Stick up/down | Intensity, respecting the existing intensity toggle and per-device participation |

The lock button enables enforced head following. It captures the video's chosen
position relative to the current head pose and uses an inner contour derived from
the screen's current physical width and aspect ratio, so a wide screen permits
more horizontal than vertical looking. Remaining inside that contour does not
move the screen. Staying outside it for the configured delay starts enforcement;
once started, further head movement retargets the screen instead of cancelling
it. Enforcement ends only after the screen catches the current pose and the head
is still. Its motion is deliberately timid at launch, firm through the middle and
slow for the final alignment. Dragging suspends following and records the new
position on release. The menu adjusts the delay (0.5–5 s), response speed,
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
grab/follow state and frame loop. `media-layer.mjs` promotes compatible flat video
to a native compositor stream; `video-surface.mjs` owns its interaction proxy and
the video-texture fallback for unsupported browsers, curvature or reversed eyes.
per-eye geometries. `format.mjs` and `motion.mjs` are hardware-free math/input seams.
`menu.mjs` owns only the additional VR settings UI.

The immersive runtime never moves or rasterizes the fullscreen HTML controls.
They remain in the normal web player and are not rendered over the video in VR.
The only VR UI is a small Three.js exit button below the screen's right edge;
its larger invisible proximity target reveals it when a controller points near
the button. Exiting cancels the frame loop and disposes controller listeners,
geometries and textures without touching the desktop DOM.

The player composition root imports only the lightweight entry module. Three.js
and the HTML renderer load when entering VR, from pinned local files under
`wwwroot/lib/vr`; normal playback does not need a CDN. No EDI API or core/device
code is changed by this mode.

Quest rendering requests WebXR Layers and, when supported, sends the pure video
through a native `XRMediaBinding` layer for direct compositor quality. A separate
transparent WebGL projection layer contains only the exit affordance and
controller pointers. Unsupported headsets fall back automatically to the existing video
texture path. Three.js framebuffer scaling and compositor foveation remain enabled,
with MSAA disabled. HTML snapshots use one shared, throttled capture budget and
CSS-pixel textures so DOM changes cannot continuously stall the XR frame loop.

## Quest diagnostics

While a session is running, `window.ediVrDiagnostics` contains the current FPS,
last and worst JavaScript frame time, video readiness, selected video stream and
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
following with micro-motion tolerance, the proximity exit button, reentry and error cleanup.
Physical headset ergonomics, decoding limits and controller compatibility still
need a headset test; the simulator does not establish those.

Reference APIs: [WebXR security](https://developer.mozilla.org/en-US/docs/Web/API/WebXR_Device_API/Permissions_and_security),
[WebXR gamepad mapping](https://www.w3.org/TR/webxr-gamepads-module-1/),
[Three.js WebXRManager](https://threejs.org/docs/pages/WebXRManager.html),
[DeoVR format documentation](https://deovr.com/documentation).
