# Concurrency and testing

## Current concurrency map

Read the concrete code before changing it; these are navigation points:

- `Edi.Init` initializes repositories sequentially, then detaches device initialization.
- `DeviceCollector.Init` initializes all providers concurrently.
- provider reconnect loops use `System.Timers.Timer` and async connection methods;
- reaction/filler transitions use one-shot `System.Timers.Timer` callbacks;
- OSR playback uses a periodic timer, cancellation sources, `Monitor`, and a semaphore;
- Handy/AutoBlow uploads replace cancellation sources and start background tasks;
- preview/recorder devices use timers for progress or disk flushing;
- configuration persistence retries file access with blocking sleeps;
- device lists and provider lists are mutable and are not uniformly synchronized.

Treat timer callbacks as concurrent even when they usually appear serialized during manual
testing. WPF property-change callbacks may require dispatcher affinity.

## Playback replacement invariant

- Device playback follows latest-command-wins semantics, like replacing an audio stream.
- Keep at most one command being invoked and one pending command per device; a newer pending
  command replaces older pending commands.
- Cancel and observe superseded playback, but do not wait for its task to finish before
  starting the replacement.
- Each playback operation must capture its own cancellation token. An older operation must
  never read the token belonging to its replacement.
- A slow device must not delay command dispatch to other devices.

## Review questions for thread-related bugs

1. What operation owns the state, and can two entry points mutate it concurrently?
2. Does a newer Play/Init/reconnect supersede an older operation?
3. Can Stop return before physical or simulated output has actually stopped?
4. Are task exceptions observed, including timer/event callbacks and fire-and-forget work?
5. Can a callback run after unload, game change, or shutdown?
6. Is cancellation source replacement atomic, and is the old source cancelled and disposed?
7. Is a mutable list enumerated while another callback adds/removes an item?
8. Can an event subscription keep a device/player alive or call it after removal?
9. Does any synchronous wait, sleep, file I/O, or HTTP call block the UI or timer thread?
10. Is cleanup idempotent when disconnect/stop/dispose happens more than once?

## Preferred implementation patterns

- Return `Task` from operations and await state-defining work.
- Use `CancellationToken` from the owning lifecycle and pass it through delays and I/O.
- Serialize a state machine with `SemaphoreSlim` when operations contain `await`.
- Use a short `lock` only for synchronous mutation/snapshot creation.
- Snapshot target devices under synchronization, then await outside the critical section.
- Use `PeriodicTimer` or a single owned loop when a timer callback would otherwise overlap.
- Use `Interlocked.Exchange` for replaceable cancellation sources, then cancel and dispose the
  previous instance outside critical work.
- Catch only expected cancellation at the operation boundary; log other failures with context.
- Unsubscribe events and dispose timers/clients when a provider or device is retired.

Do not mechanically replace every timer or detached task. First identify the required timing
and lifecycle contract, then choose the smallest pattern that enforces it.

## Test project guidance

Use the existing `Edi.Core.Tests` xUnit project targeting `net8.0`. Keep its tests
hardware-free and parallel-safe. Reuse `PlayerTestRig`, `RecordingDevice`,
`RecordingHttpMessageHandler`, and `TestData/Galleries` before creating another test harness.

Do not introduce a mocking framework unless it materially simplifies multiple tests. Small
fake implementations of `IDevice`, `IDeviceProvider`, `IPlayer`, repositories, transports, or
HTTP handlers often express the contract more clearly.

## High-value first tests

### Playback state

- gallery/reaction/filler transitions;
- non-loop timers and reaction restoration at the correct seek;
- hard pause versus normal pause;
- repeated Play where the latest command wins;
- Stop during playback and Stop repeated twice;
- missing/disabled gallery behavior.

### Channels and devices

- no channel selects all channels;
- a named channel affects only assigned devices;
- device load gives unique names and restores valid config;
- invalid/`None` variants and zero ranges stop output;
- unload removes playback subscriptions and prevents later callbacks.

### Repositories and configuration

- definition and filename/tag parsing;
- variant and axis discovery;
- relative gallery-path resolution;
- game/user config precedence;
- atomic behavior under concurrent saves;
- reinitialization after selecting another game.

### Provider boundaries

- connect failure does not crash unrelated providers;
- reconnect does not create duplicate devices or overlapping attempts;
- cancellation stops pending upload/play work;
- HTTP status and malformed payload failures are surfaced and logged;
- serial/UDP/MQTT commands match protocol formatting without opening real ports.

## Deterministic async tests

- Coordinate with `TaskCompletionSource` using
  `TaskCreationOptions.RunContinuationsAsynchronously`.
- Use cancellation-based test timeouts only as a final deadlock guard.
- Expose/inject a clock, delay, timer, transport, or HTTP handler at the boundary when time
  otherwise must pass.
- Assert state transitions and captured commands, not thread IDs or wall-clock precision.
- For races, block one operation at a known seam, start the competing operation, release the
  seam, and assert the specified winner.
- Avoid `Thread.Sleep` and arbitrary `Task.Delay`; they create slow, flaky tests and do not
  prove ordering.

## Verification levels

- Pure/domain change: focused tests, all test projects, solution build.
- Async/state-machine change: focused deterministic race/cancellation tests plus repeated run.
- Provider protocol change: fake transport contract tests plus build; live hardware only when
  explicitly requested.
- WPF binding change: tests where practical, solution build, and manual UI run only when
  explicitly requested.
- REST contract change: controller/host integration test and Swagger/route inspection.
