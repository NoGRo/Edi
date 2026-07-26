# External integration references

Sources checked on 2026-07-26. Online APIs and packages evolve; confirm the pinned package,
firmware branch, and local implementation before applying current examples.

## EDI public contract

- Repository and README: <https://github.com/NoGRo/Edi>

The README describes EDI as a standalone C# Windows application controlled by REST, with named
galleries, reaction/filler modes, variants, multi-axis scripts, multi-channel routing, device
ranges, and game/user configuration. Use it to understand intended behavior. Use local
controllers and code as the executable source of truth.

## Buttplug and Intiface

- Developer guide: <https://buttplug.io/docs/dev-guide/>
- Connectors: <https://buttplug.io/docs/dev-guide/writing-buttplug-applications/connectors/>
- C# API: <https://buttplug-csharp.docs.buttplug.io/api/>
- Intiface Central: <https://intiface.com/>

The official architecture separates the Buttplug client from an Intiface/Buttplug server.
WebSocket is the common transport; port `12345` is conventional but should remain configurable.
Connection loss, an unavailable server, and client/server protocol mismatch are expected error
paths.

EDI currently pins `Buttplug` 4.0.0. Do not copy APIs from a newer package without checking
compatibility and planning the upgrade explicitly.

## The Handy

- API v3 Swagger: <https://www.handyfeeling.com/api/handy-rest/v3/docs/>
- API v3 specification: <https://www.handyfeeling.com/api/handy-rest/v3/docs/spec.yaml>

The v3 API requires authentication and supports server-sent events plus multiple protocols,
including HSP. The official page labels v3 beta and notes that firmware older than v4 is not
supported by v3. EDI selects a concrete device implementation after firmware/API detection;
preserve that compatibility split unless an explicit migration removes it.

Never log or commit Handy keys or authorization material.

## AutoBlow

- HTTP API v1: <https://developers.autoblow.com/reference/http-api-v1-autoblow/>
- JavaScript SDK guide: <https://developers.autoblow.com/guides/autoblow-js-sdk/>

The official API exposes sync-script load/start/stop behavior and requires a device token.
Treat device-not-connected, motor-stuck, overheating, and generic error states as meaningful
runtime results. Keep cluster/base-address discovery and token headers inside the provider.

Never copy example or real device tokens into source, tests, output, or skill references.

## OSR and TCode

- OSR2 Arduino reference firmware: <https://github.com/multiaxis/OSR2-Arduino>
- TCodeESP32 firmware: <https://github.com/jcfain/TCodeESP32>

TCode firmware and axis capabilities vary. EDI supports serial and UDP and maps funscript axes
to device positions. Verify the target firmware/TCode version, supported axes, value range,
update interval, and line framing before modifying emitted commands. Test formatting through a
fake `IOSRConnection`; do not require a COM port.

## MQTTnet

- Official repository and samples: <https://github.com/dotnet/MQTTnet>
- NuGet package: <https://www.nuget.org/packages/MQTTnet/>

EDI currently pins `MQTTnet` 5.0.1.1416. Preserve async publishing and cancellation semantics.
Connection/reconnection and QoS behavior belong at the MQTT boundary, while gallery semantics
belong in the player/device layer.

## Funscript

- Community format reference: <https://www.funscript.wiki/>

A funscript is JSON containing timestamped position actions; ecosystem extensions add axes,
metadata, and chapters. There is no single stable vendor specification covering every
extension. For EDI parsing, treat `FunScriptFile`, `Discover`, repository code, fixtures, and
tests as authoritative, and document any compatibility decision with a representative fixture.
