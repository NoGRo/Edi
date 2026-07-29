# Changelog

**[Version 1.0.2]**

**New Features**

Added Handy firmware v3 support through the HSP protocol
Added direct Handy Bluetooth Low Energy connection and automatic rediscovery
Added AutoBlow and VacuGlide 2 automatic detection and playback support
Added configurable device offset for Handy, AutoBlow, and VacuGlide 2
Added OBS recording integration to generate chapter-based funscripts
Added multiple configurable funscript recorders with channel and variant support
Added a saved game manager to add, rename, edit, remove, and switch games
Added per-channel playback preview and recorder controls

**Improvements**

Redesigned the WPF interface for better accessibility and playback control
Improved Handy v3 synchronization, time handling, reconnection, and legacy looping
Improved recorder output for seek, loop, range, variant, and gallery transitions
Galleries and repositories can now reload on demand without restarting EDI
Improved device lifecycle, asynchronous playback, and multi-channel synchronization
Added automatic UI migration for previously saved games
Added extensive automated coverage for devices, galleries, players, and recorders

**Bug Fixes**

Fixed device reconnection and cleanup across supported providers
Fixed empty OSR UDP addresses being treated as valid endpoints
Fixed preview range, variant, channel, and gallery refresh behavior
Fixed overlapping playback commands, stale timers, and pause/resume race conditions
Fixed application and preview window shutdown synchronization

**Compatibility**

The WPF application now requires Windows 10 version 2004 or newer

**Contributors**

Javier Crisolini: initial recorder concatenation, range, variant, and stability improvements
[@to4st](https://discuss.eroscripts.com/u/to4st): initial OBS chapter generator, configuration, and chapter filtering
[Kevin Liu / Seele-Vollerei32](https://github.com/NoGRo/Edi/pull/8): WPF accessibility redesign and OSR UDP validation fix

**[Version 1.0.1]**

**Bug Fixes**

Exposed all files in the gallery folder through `Edi/Assets/[FilePath]`
