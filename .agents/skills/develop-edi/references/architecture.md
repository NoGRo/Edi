# EDI architecture

## Product model

EDI is a standalone Windows-oriented .NET application that lets a game control intimate
devices through a REST API. A game sends gallery names and playback operations; EDI resolves
pre-scripted assets, applies reaction/filler/channel rules, and delegates protocol-specific
commands to devices.

The central abstraction is deliberately content-based rather than command-based:

```text
REST/UI/console -> IEdi -> IPlayerChannels -> IPlayer -> IDevice
                         |                         |
                         v                         v
                  DefinitionRepository     device-specific repository
                                           and transport/API
```

## Solution layout

- `Edi.Core`: reusable domain, service, API, configuration, gallery, player, and device code.
- `Edi.Wpf`: Windows launcher and interactive UI; builds as `Edi`.
- `Edi.Mvc`: ASP.NET Core MVC host and browser player.
- `Edi.Console`: command-line host.
- `Edi.Core.Tests`: xUnit tests with hardware-free gallery, player, concurrency, and Handy
  fixtures.
- `Edi.sln`: the four application/library projects plus the .NET 8 test project.

## Composition and startup

`EdiRegistration.AddEdi` is the composition root for the core:

- registers configuration and repositories as singletons;
- registers players and `DeviceCollector`;
- registers each `IDeviceProvider`;
- exposes `IEdi` as `Edi`;
- starts `EdiHostedService`, which creates the embedded REST application through `ApiBuilder`.

`Edi.Init` resolves the selected gallery/config path, initializes all repositories, resets
channels, and starts provider initialization. `DeviceCollector.Init` initializes providers
concurrently with `Task.WhenAll`.

When changing startup, inspect:

- `Edi.Core/EdiRegistration.cs`
- `Edi.Core/Services/Edi.cs`
- `Edi.Core/Services/EdiHostedService.cs`
- `Edi.Core/Services/ApiBuilder.cs`
- the selected host's `Program.cs`/`App.xaml.cs`

## Galleries and definitions

Repositories convert files into device-appropriate gallery objects:

- `DefinitionRepository`: canonical names, types, duration, loop, and variants.
- `FunscriptRepository`: timestamped position/axis scripts.
- `IndexRepository`: bundles/index data used by remote script devices.
- `AudioRepository`: audio galleries for eStim.

`DefinitionGallery.Type` drives three playback modes:

- `gallery`: primary playback;
- `reaction`: temporary playback followed by resynchronization;
- `filler`: idle/background playback.

`ReactionGalleryFillerPlayer` owns these transitions and its stop timers. `SyncPlayback`
calculates seek/resume timing. Keep duration, loop, seek, pause, and reaction restoration
semantics together when testing a change.

Files and folders may encode variants and axes. `Discover` and the repository classes are the
source of truth for parsing conventions.

## Playback and channels

Important abstractions:

- `IPlayer`: play, stop, pause, resume, intensity.
- `IPlayerChannels`: the same operations plus channel selection/reset.
- `DevicePlayer`: synchronizes a set of devices to one logical playback.
- `ReactionGalleryFillerPlayer`: decorates playback with gallery-type state.
- `MultiChannelPlayer`: owns independent channel players and device assignment.
- `CompositePlayer`: forwards operations to multiple channel-aware players.
- `ChannelManager<T>`: selects target channels; no channel means all configured channels.

Do not call devices directly from a controller or UI merely to simplify routing. Doing so can
skip pause state, reaction restoration, filler selection, channel filtering, and logging.

## Devices and providers

`IDeviceProvider.Init` discovers/connects devices. Providers must call
`DeviceCollector.LoadDevice` and `UnloadDevice`. `IDevice` exposes named-gallery playback,
variants, readiness, channel, and stop. `IRange` adds min/max output limits.

Current families:

- `Buttplug`: Intiface/Buttplug WebSocket devices, including vibration/linear features.
- `Handy`: firmware-detected v2/v3 implementations, server-time sync, bundle/HSP playback.
- `AutoBlow`: remote sync-script API and bundles.
- `OSR`: serial or UDP TCode playback with multiple axes.
- `Mqtt`: publishes gallery-derived commands through MQTT.
- `EStim`: plays audio galleries through NAudio.
- `Simulator`: preview and recording devices for hardware-free development.

Most devices derive from `DeviceBase<TRepository,TGallery>`. OSR implements `IDevice`
directly because its timing and multi-axis command loop differ.

## Configuration and persistence

`ConfigurationManager` combines:

- a game-level `EdiConfig.json`;
- `%LOCALAPPDATA%\Edi\UserConfig.json` for types marked `[UserConfig]`.

It caches typed config objects, updates existing instances when the selected game changes, and
writes property changes back to the appropriate file. Config classes marked `[GameConfig]`
participate in generated game config.

Configuration writes, reflection-based discovery, and cached object identity are observable
behavior. Tests that change this area should use a temporary directory and must never read or
overwrite the developer's real `%LOCALAPPDATA%\Edi` files.

## REST surface

Controllers in `Edi.Core/Controllers` expose playback, definitions/assets, device selection,
ranges, variants, and channels. Both visible POST actions and hidden GET compatibility actions
may exist. When changing a route, inspect Swagger filters and both controller surfaces so game
clients do not silently break.

The repository README documents the intended public API, but controller attributes and tests
must be treated as the executable contract.

## Known baseline

On 2026-07-26, with .NET SDK 9.0.307, this command succeeded for all four projects:

```powershell
dotnet build .\Edi.sln --configuration Debug --nologo --disable-build-servers --maxcpucount:1
```

The baseline had 27 warnings and 0 errors. Most warnings were WPF nullable-reference warnings;
there was also a generated-entry-point warning and a Fody `PrivateAssets` warning. Do not
attribute existing warnings to a new change without comparing against the baseline.

Keep solution builds single-node: parallel solution builds can race with WPF's temporary
project and remove `Edi.Wpf/obj/project.assets.json`. If that file is missing, force-restore
`Edi.Wpf.csproj` and rebuild with `--maxcpucount:1`.

The player test suite can be run independently with:

```powershell
dotnet test .\Edi.Core.Tests\Edi.Core.Tests.csproj --configuration Debug --nologo
```

Fixtures under `Edi.Core.Tests/TestData/Galleries` exercise definitions and real funscript
parsing without connecting to physical devices.
