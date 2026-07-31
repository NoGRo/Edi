---
name: develop-edi
description: Develop, debug, refactor, build, and test the Easy Device Integration (EDI) C#/.NET solution for videogame-controlled intimate devices. Use for changes involving Edi.Core, WPF/MVC/console hosts, gallery and funscript playback, reaction/filler behavior, multi-channel routing, device providers (Buttplug/Intiface, Handy, AutoBlow, OSR/TCode, MQTT, eStim, simulators), configuration, REST endpoints, synchronization, timers, cancellation, thread safety, or automated tests in this repository.
---

# Develop EDI

Work from the repository containing `Edi.sln`. Treat the current source as authoritative and
use the bundled references as orientation, not as a substitute for reading affected code.

## Load only the needed context

- Read [references/architecture.md](references/architecture.md) before changing cross-cutting
  behavior, dependency injection, galleries, players, channels, providers, or configuration.
- Read [references/concurrency-and-testing.md](references/concurrency-and-testing.md) for
  threading, timers, cancellation, playback synchronization, shutdown, or test work.
- Read [references/external-integrations.md](references/external-integrations.md) before
  changing a device protocol or upgrading its package/API.

## Follow the development workflow

1. Run `git status --short --branch` and preserve unrelated user changes.
2. Locate the behavior from its public entry point through players/repositories to the device
   boundary. Read interfaces and registrations as well as the concrete class.
3. State the expected behavior and identify ownership of mutable state, background work,
   cancellation tokens, timers, event subscriptions, and disposable resources.
4. Add or update tests around the lowest hardware-free seam that proves the behavior.
5. Implement the smallest coherent change. Preserve the gallery-name abstraction: callers
   request named galleries; device implementations translate them into protocol commands.
6. Build from the solution root:

   ```powershell
   dotnet build .\Edi.sln --configuration Debug --nologo --disable-build-servers --maxcpucount:1
   ```

7. Run every test project found in the solution. If none exists and the change is behavioral,
   create an `Edi.Core.Tests` project unless the requested task is explicitly limited.
8. Review the final diff for accidental config, credentials, certificates, generated output,
   unrelated formatting changes, and unnecessary growth in `Edi.Core`.

## Keep the implementation small

- Treat source-code volume as a design constraint. For a localized request, prefer changing or
  deleting existing code over adding infrastructure. Reuse the current base classes, interfaces,
  configuration objects, events, and extension points before creating anything new.
- Do not create a new base class, interface, service, manager, configuration container, wrapper,
  callback abstraction, or helper class for a behavior that has one consumer or can fit clearly in
  its existing owner. A possible future reuse is not sufficient justification.
- Property observation does not by itself justify another configuration graph. First check whether
  the existing device, provider, `DeviceBase`, or existing configuration object can observe the
  property directly. Add another object only when the persisted schema or an already-established
  family boundary requires it.
- Keep a localized fix localized. Do not turn it into a framework, migrate neighboring devices, or
  generalize unrelated code unless the user explicitly requests that broader refactor.
- Before finishing, inspect `git diff --stat` and the added/deleted line count for `Edi.Core`. A
  localized change adding roughly 100 or more net lines to `Edi.Core` is presumed over-engineered:
  stop, simplify, and reuse existing code. Exceed that size only for an explicitly requested new
  subsystem or when a smaller correct implementation is impossible, and explain the reason to the
  user before proceeding.
- Tests should be proportional to the behavior and should reuse existing fixtures/fakes. Do not
  compensate for an oversized production design with hundreds of lines of test scaffolding.

## Preserve project invariants

- Keep reusable behavior in `Edi.Core`; keep WPF, MVC, and console projects as hosts/adapters.
- Register new services and providers through the existing dependency-injection extensions.
- Load and unload devices through `DeviceCollector` so naming, saved settings, events, ranges,
  variants, and channels stay consistent.
- Keep `DeviceCollector` device-agnostic. It may restore shared capability properties or call
  a device-owned configuration hook, but protocol-specific normalization, change observation,
  command dispatch, retries, and error handling belong in the existing `DeviceBase` or the
  concrete device/provider boundary. Extend `DeviceBase` for shared device behavior; do not
  introduce parallel capability-specific base classes. Higher layers may assign an advertised
  capability such as range or offset; they must not apply its device command themselves.
- Enforce these device-configuration layer boundaries:
  - `DeviceCollector` owns only generic lifecycle, naming, configuration attachment/removal,
    and restoration of common capabilities. It must not import a device-family namespace,
    test a concrete device type, normalize family data, or issue a family command.
  - `DeviceConfiguration` owns selection and persistence of common capabilities. It must not
    import a device-family namespace or expose methods named for DG-Lab, OSR, Handy, AutoBlow,
    or any other implementation. Hiding a concrete type check or family mutation inside an
    `Action<DeviceConfig>`, callback, reflection helper, or generic-looking wrapper does not
    remove the coupling and is not allowed.
  - Shared configuration types may contain only shared capabilities. In particular,
    `DeviceConfig` may contain variant, channel, range, offset, or another capability defined
    in the shared device layer; it must never reference `DgLabChannelConfig`,
    `OsrDeviceConfig`, or any other family-owned type.
  - Family settings live in configuration containers declared in that family's namespace.
    The family provider selects the entry for a loaded device and attaches/removes it at the
    family device boundary. The concrete configuration object raises `PropertyChanged`; the
    owning device subscribes, normalizes, and applies it, and unsubscribes on replacement or
    unload. A family-specific host editor may mutate that existing object, but shared
    collectors, services, controllers, and DTOs must never transport or persist its fields.
  - `DevicesController` and `DeviceDto` expose only common interfaces or explicitly designed
    protocol-neutral capabilities. Do not add routes, request bodies, response fields, casts,
    or Swagger contracts named for a device family. A generic `/Offset` route is valid because
    offset is advertised by a common capability; `/DgLab` and `/Osr` are not.
- Route playback through `IPlayer`/`IPlayerChannels`; do not bypass reaction/filler or channel
  semantics from controllers or UI code.
- Keep `EdiConfig.json` game-specific and `UserConfig.json` user-specific according to the
  configuration attributes and `ConfigurationManager`.
- Treat missing hardware and disconnected services as normal runtime states. Do not make EDI
  startup depend on every provider being available.

## Apply hardware and secret safety

- Do not start WPF/MVC hosts or send commands to physical devices unless the user explicitly
  requests a live test.
- Prefer `PreviewDevice`, `RecorderDevice`, fake providers, fake transports, and local HTTP
  handlers in automated verification.
- Never expose or copy device keys, tokens, certificate passwords, personal gallery paths, or
  other values from configuration. Use obvious placeholders in tests and documentation.
- Do not modify or regenerate `certificate.pfx`/`certificate.crt` unless explicitly requested.

## Handle concurrency deliberately

- Avoid `async void` except framework event handlers; make event handlers observe and log
  failures.
- Await work that defines observable state transitions. If work must be detached, give it an
  owner, cancellation path, exception observation, and shutdown behavior.
- Do not hold `lock` or `Monitor` across `await`. Guard shared collections consistently and
  snapshot them before asynchronous fan-out.
- Give each timer and `CancellationTokenSource` one clear owner; stop, cancel, unsubscribe, and
  dispose them during replacement or shutdown.
- Make repeated `Init`, `Play`, `Stop`, reconnect, and configuration changes safe and
  idempotent where practical.
- Use deterministic coordination in tests; do not use arbitrary sleeps to prove ordering.

## Keep external protocol work version-aware

Check the package versions in `Edi.Core.csproj` and the concrete provider before following
current online documentation. EDI may intentionally target an older protocol. Record protocol
assumptions in tests and keep HTTP/WebSocket/serial details behind device/provider boundaries.
