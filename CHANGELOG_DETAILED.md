# Detailed Changelog

## Review Scope

- Branch: `TestConCodex`
- Baseline: `c59d542` (`1.0.1`)
- Reviewed through: `c0764d3` (`1.0.2`)
- Range: `c59d542..c0764d3`
- History reviewed: 38 commits, including 4 merge commits
- Pending working-tree changes are not included

## [Version 1.0.2]

### New Features

#### Handy v3 and Bluetooth

- Added automatic firmware detection and selection between the legacy Handy protocol and HSP for firmware v3.
- Added HSP playback for uploaded funscripts, play, stop, seek, looping, and server-time synchronization.
- Added direct Bluetooth Low Energy discovery and playback on supported Windows systems.
- Added automatic Bluetooth recovery when a Handy disconnects and starts advertising again.
- Added a shared configurable offset from `-7000 ms` to `+7000 ms`, applied to internet and Bluetooth connections.
- Added support for multiple configured Handy connection keys.

Related commits: `5544806`, `826fc1d`, `57aa193`, `791cdd8`, `725cd33`, `3cd61b5`

#### AutoBlow and VacuGlide 2

- Added automatic identification of AutoBlow and VacuGlide 2 devices from configured connection keys.
- Added VacuGlide 2 as a dedicated device type while sharing the AutoBlow playback implementation.
- Added offset updates, reconnect handling, and cleanup of disconnected devices.
- Added support for multiple distinct connection keys.

Related commit: `8d21715`

#### OBS Chapter Generator

- Added optional OBS WebSocket integration controlled by `UseObsChapterGenerator`.
- Tracks EDI gallery playback while OBS is recording.
- Generates a `.funscript` beside the completed OBS recording.
- Creates chapter metadata from the longest recorded occurrence of each gallery.
- Added configurable OBS WebSocket URL, minimum chapter length, and chapter-end buffer.

Related commits: `edd8442`, `a543209`, `5766d3d`, `4acffb1`, `a612a27`, `314b297`

#### Recording System

- Added multiple named recorder devices configured through the recorder settings.
- Added recorder creation and removal from the preview window.
- Added explicit start, stop, save, and recordings-folder controls.
- Added channel and variant selection per recorder.
- Added a chronological recording timeline that preserves gallery transitions, seek positions, loops, ranges, and pauses.
- Recorder devices are hidden from the normal active-device list and managed in their own panel.

Related commits: `26dd80c`, `40db94f`, `41b0dc8`, `63af123`, `b297b30`, `ef8bb27`, `c0764d3`

#### Saved Game Manager

- Added friendly names for saved games.
- Added controls to add, edit, rename, repoint, remove, and switch games.
- Accepts `EdiConfig.json`, `Definitions.csv`, and the legacy `Definition.csv` filename.
- Prevents duplicate saved paths and reports missing or invalid game files.
- Automatically upgrades legacy entries whose displayed name was previously the full path.

Related commit: `cc8f083`

#### Preview and Channel Tools

- Added a dedicated simulator base used by preview and recorder devices.
- Added per-channel preview selection.
- Added live gallery name, type, loop, seek, current time, duration, channel, and variant information.
- Added channel-aware preview refresh when galleries or repositories change.

Related commits: `a035923`, `6c2ca35`, `27e3921`, `c0764d3`

### Improvements

#### WPF Interface

- Reorganized the main window into game, connection, playback, device, and status areas.
- Added clearer labels, tooltips, icons, resizing behavior, and readiness indicators.
- Added direct controls for intensity, filler, gallery, reaction, playback, preview, reconnection, API documentation, and the output folder.
- Added safer asynchronous shutdown of playback and the preview window.
- Improved configuration binding for device range, variant, channel, and connection settings.

Related commits: `ac0e9b6`, `e76abd2`, `cc8f083`

#### Gallery and Repository Loading

- Repositories are now created only when a device or feature needs them.
- Repository dependencies initialize in order and follow gallery path changes.
- Gallery changes can reload providers and resynchronize active devices without restarting EDI.
- Devices track repository revisions so playback uses the current gallery data.

Related commits: `77291c0`, `27e3921`

#### Playback and Device Lifecycle

- Serialized device state transitions to prevent overlapping play, stop, range, and loop operations.
- Added cancellation and observation for superseded playback tasks.
- Improved reaction, filler, and primary-gallery transition scheduling.
- Improved multi-channel startup and dynamic channel synchronization.
- Added `CompositePlayer` support for forwarding commands to multiple channel players.
- Providers now expose asynchronous initialization and disconnection for reliable reconnect and shutdown.
- Device removal now stops playback and releases provider resources.

Related commits: `aa5c70e`, `917c7ad`, `4c4aa51`, `bb90ee3`, `725cd33`, `87a0cbf`, `27e3921`

#### Handy Playback

- Improved server-time synchronization for both internet and Bluetooth transports.
- Improved v3 upload, play, seek, loop, and command timing.
- Improved legacy loop handling and funscript boundary generation.
- Debounced offset updates and applied the newest value across active Handy-compatible devices.
- Improved reconnect behavior while avoiding duplicate device instances and Bluetooth sessions.

Related commits: `5544806`, `826fc1d`, `57aa193`, `791cdd8`, `725cd33`, `3cd61b5`, `87a0cbf`

### Bug Fixes

- Fixed empty OSR UDP addresses being parsed as valid endpoints.
- Fixed recorder provider device registration and cleanup.
- Fixed preview and recorder ranges not being applied consistently.
- Fixed default variant and channel selection for newly loaded devices.
- Fixed channels not being restored correctly during startup.
- Fixed stale gallery references after changing or reloading a game.
- Fixed playback races caused by unobserved asynchronous device commands.
- Fixed outdated stop timers interrupting newer gallery or reaction playback.
- Fixed Handy Bluetooth time calculations and reconnect behavior.
- Fixed shutdown races between the main window, playback, and preview window.

Related commits: `41b0dc8`, `63af123`, `aa5c70e`, `ea42612`, `eeeed20`, `bb90ee3`, `791cdd8`, `725cd33`, `27e3921`, `cc8f083`

### Compatibility and Dependencies

- Updated the WPF target from `net8.0-windows7.0` to `net8.0-windows10.0.19041`.
- `Edi.Core` now targets both `net8.0` and `net8.0-windows10.0.19041`.
- Added Bluetooth LE and Protocol Buffers dependencies required by Handy HSP.
- Added the Handy protocol definitions used to generate HSP message types.

Related commit: `57aa193`

### Automated Tests and Development Support

- Added the `Edi.Core.Tests` project to the solution.
- Added coverage for AutoBlow discovery and VacuGlide selection.
- Added coverage for device concurrency, cancellation, ranges, loops, and stale commands.
- Added coverage for provider lifecycle and reconnection.
- Added coverage for Handy legacy, v3, Bluetooth, offset, and time synchronization behavior.
- Added coverage for repository creation, dependency ordering, and gallery path changes.
- Added coverage for multi-channel, OBS, reaction, filler, and gallery playback.
- Added coverage for preview devices, recording timelines, and saved game management.
- Added hardware-free test doubles and gallery fixtures.
- Added the EDI development guide and architecture, concurrency, testing, and integration references.

Primary related commits: `4c4aa51`, `bb90ee3`, `826fc1d`, `57aa193`, `77291c0`, `725cd33`, `ef8bb27`, `8d21715`, `314b297`, `a035923`, `27e3921`, `cc8f083`, `c0764d3`

## [Version 1.0.1]

### Bug Fixes

- Exposed gallery files through `Edi/Assets/[FilePath]`, including range-enabled media responses.

Related commits: `48f81f3`, `c59d542`
