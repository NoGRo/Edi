# **Easy Device Integration (EDI) for Videogames**

Easy Device Integration (EDI) is a Windows application developed in C# that synchronizes game events with interactive sex toys. Its modular and simple architecture makes it a powerful, flexible, and easy-to-integrate tool for any game.

---

### Why Use EDI?

- Compatible with Buttplug.io, Lovense, OSR, XToys, eStim via mp3 file, Handy, AutoBlow, and more.
- SOLID architecture decoupled from the game engine.
- Simple HTTP API control (`Play`, `Stop`, `Pause`, etc.).
- Expandable galleries synced to a single video.
- Multi-device, multi-axis support, and customizable variants.
- Smooth and synchronized funscript playback within your game.

---

### Gallery Types in EDI

This section describes how EDI handles different types of galleries, which represent key game moments that trigger device actions.

- **Gallery:** main type. Plays immediately with `Play`, used for primary or long scenes.
- **Reaction:** short events or quick responses (e.g., clicks, hits). Plays instantly and then EDI resyncs with the previous gallery.
- **Filler:** plays when no other gallery is active, simulating background or passive motion.

All types support the `loop` property as `true` or `false`.

---

### Key Integration Features

#### Gallery Definition

- Galleries are defined using a `Definition.csv` file, chapters in OFS, or can be auto-generated as `definition_auto.csv` based on filenames.
- Auto-generation can be enabled in the config file (`EdiConfig.json` ).
- Supported types: `gallery`, `reaction`, `filler`.
- If no type or loop is specified, EDI assumes `gallery` with `loop = true`.
- You can also define gallery types using tags in the file name to avoid editing the CSV manually.

##### Chapters or Filename Tag Examples:

- `filler_3[filler].funscript` → type `filler`, loop `true`
- `golpe_critico[reaction][nonLoop].funscript` → type `reaction`, loop `false`
- `go_home[filler][nonLoop]` → chapter of type `filler`, loop `false`

Tags can be placed in any order and override default behavior.

#### Gallery Variants

- Variants can be defined in the filename (`enemy_attack2.intense.funscript`) or in the containing folder (`intense/enemy_attack2.funscript`).
- Variants can also be defined per device (e.g., `enemy_attack2.vibrator.funscript`).

#### Multi-Axis Support

EDI allows full use of multi-axis or multi-motor devices via naming conventions in funscript files.

- Axes are declared in the filename: `.linear`, `.twist`, `.roll`, `.vibrate`, etc.
- EDI auto-detects and applies these axes for compatible devices like OSR.

##### Examples:

- `ataque_fuerte.intense.linear.funscript` → variant `intense`, axis `linear`
- `ataque_fuerte.intense.twist.funscript` → axis `twist`

---

### Bundle Management for Handy and AutoBlow

EDI groups all galleries into bundles for efficient loading into devices like Handy and AutoBlow.

- Bundles reduce latency by grouping related galleries.
- Defined in `BundleDefinition.csv` under `C:\Users\{User}\AppData\Local\Edi`
- The `default` bundle contains all galleries not explicitly grouped.
- When a gallery from another bundle is needed, EDI uploads that bundle dynamically.
- A gallery can be in multiple bundles (useful for `filler` or `reaction`).
- If a bundle exceeds 1MB, split it into smaller ones.

Example:
```
-Intro
intro1
intro2

-Missions
attack1
attack2
reaction_click
```

---

### Configuration File (`EdiConfig.json`)

#### `Gallery` Section

- `GalleryPath`: folder path containing funscripts.
- `GenerateDefinitionFromChapters`: `true` to generate `Definition.csv` from chapters.
- `GenerateChaptersFromDefinition`: generates chapters from `Definition.csv`.

#### `GalleryBundler` Section

This section controls auto-repeat and timing between galleries. All values are in milliseconds.

- `MinRepeatDuration`: minimum time before a command is resent to the device (e.g., Handy) during loop (increases bundle size).
- `RepeatDuration`: extra duration to offset lag between commands (increases bundle size).
- `SpacerDuration`: pause between one gallery ending and the next starting (does NOT increase bundle size).

#### `Edi` Section

- Toggle active gallery types: `Filler`, `Gallery`, `Reactive`
- `ExecuteOnReady`: launches the game when EDI is ready.
- `UseHttps`, `UseLogs`: debugging and security options.

#### `Devices` Section

- Devices are configured individually by name.
- You can assign a `Variant`, and define `Min` and `Max` intensity.

---



### HTTP API Control

This API lets your game control playback, intensity, and device settings using simple HTTP commands. All endpoints accept both POST and GET requests.

#### Playback

- `POST /Edi/Play/{name}?seek=0`: plays a gallery by name, optionally from a specific point (milliseconds).
- `POST /Edi/Stop`: stops current playback.
- `POST /Edi/Pause`: pauses device output.
- `POST /Edi/Resume?AtCurrentTime=false`: resumes from pause. With `AtCurrentTime=true`, syncs to current video time.

#### Intensity

- `POST /Edi/Intensity/{max}` (0–100%): sets global intensity across devices.

#### Devices

- `GET /Devices`: lists connected devices.
- `POST /Devices/{deviceName}/Variant/{variantName}`: assigns a variant to a device. Using `None` stops that device.
- `POST /Devices/{deviceName}/Range/{min}-{max}`: sets device intensity range. If both values are 0, the device is stopped.

#### Content

- `GET /Edi/Definitions`: returns all available galleries.
- `GET/POST /Edi/Assets`: upload or list funscripts and audio files.

---

### Graphical Interface (EDI Launcher)

EDI includes a user-friendly GUI to manage essential functions easily.

![image|690x401](upload://qn0Noj1rC5OaYBX2dkZ6lukiJoX.png)

- Select connected devices and assign variants.
- Toggle active gallery types: Gallery, Filler, Reaction.
- Adjust intensity with slider.
- Manual playback controls.
- Live preview of device response, useful for testing without hardware.
- Link to API Swagger for testing and debugging.

### Device Control Model in EDI

All interaction with devices in EDI is handled through pre-written galleries. When a command is sent to a device, it's almost always the name of a gallery, such as `sex_scene_mari-1` , `final_boss_fight-3` , or `filler-5` . Each device implementation within EDI knows how to retrieve and play that gallery.

Raw device commands like `vibrate` or `linear movement` are isolated inside the device implementation. The game never needs to issue low-level commands—just the gallery name.

This architecture makes the control layer entirely independent from the game itself. For example, one can add a full set of galleries in a `FullGalleries.mp3` file to support an eStim device without touching the integration or modifying any code.

The same principle allows adding future devices (e.g., a brainwave stimulator) by simply preparing galleries and placing files in the folder—no code changes needed.

EDI also solves common funscript playback issues like overly persistent strokes or inconsistent behavior across devices. Since devices all use the same unified player, improvements benefit all integrations automatically.

**Limitation:** EDI cannot play dynamic content. Everything must be pre-scripted. However, this can be worked around by preparing multiple gallery variants with varying intensities, such as:

* `Lori_Attack-level-1`
* `Lori_Attack-level-2`
* `Lori_Attack-level-3`

---