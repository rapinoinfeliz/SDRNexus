# ADR 0001: Thin SDR# plugin with a separate Windows Bridge

- Status: Accepted
- Date: 2026-08-20
- Owners: SDRNexus / DXNexus
- Decision scope: Windows client architecture, security boundary, and first release

## Context

DXNexus needs to consume the currently tuned SDR# frequency and return useful
station context: candidates, schedules, distance, bearing, reception estimate,
received status, and wishlist status. It must later support quick logging and a
visible-spectrum companion.

The SDR# plugin runs inside the SDR# process. A crash, blocked UI thread,
unbounded allocation, or slow network operation in the plugin can disrupt the
receiver, audio, spectrum, and waterfall. Authentication tokens also should not
be held by an in-process extension when a smaller trust boundary is possible.

The current SDR# SDK exposes the state needed for the first release through
`ISharpControl`, including tuned and center frequencies, detector type, filter
bandwidth, playback state, RDS fields, and relative visual signal readings.
Spectrum snapshots and custom paint events exist, but are not required for the
first release.

DXNexus already owns the canonical radio catalog, receiver profiles, listening
points, reception models, wishlist, and logbook. Those models must remain the
single source of truth instead of being reimplemented in C#.

## Decision

Use three independently testable components:

```text
Airspy HF+ Discovery / SDR#
              |
              | ISharpControl
              v
     DXNexus SDR# Plugin
              |
              | local, duplex named pipe
              v
   DXNexus Bridge for Windows
              |
              | outbound HTTPS; WSS in a later phase
              v
        DXNexus Cloud API
```

### SDR# plugin boundary

The plugin is a thin host adapter and presentation surface. It will:

- implement the official SDR# plugin interfaces;
- observe frequency, mode, bandwidth, playback, RDS, and supported relative
  display metrics;
- debounce rapidly changing tuner state;
- communicate only with the local Bridge;
- display connection state, candidates, and explicit user actions;
- marshal host mutations to the SDR# UI thread;
- use bounded queues and release all subscriptions in `Close()`.

The plugin will not:

- receive or store the user's DXNexus password;
- own cloud credentials;
- access D1 or R2 directly;
- open a TCP/HTTP listener;
- perform HTTP, JSON parsing, or geographic calculations on the UI/DSP path;
- transmit raw IQ, FFT, waterfall, or audio automatically;
- update its loaded DLL while SDR# is running.

### Windows Bridge boundary

The Bridge is a per-user tray application. It will:

- expose a duplex named pipe restricted to the current Windows user SID;
- perform browser-assisted device pairing;
- protect renewable credentials with Windows DPAPI (`CurrentUser`);
- own HTTPS/WSS, reconnect, backoff, and request cancellation;
- keep an access-controlled local SQLite queue for explicit durable mutations
  such as log creation and wishlist changes;
- discard transient tuner/signal states instead of replaying them;
- verify signed updates and update the plugin only while SDR# is closed.

The Bridge is not a Windows Service and will not expose a localhost web server.

### DXNexus cloud boundary

DXNexus will expose a separately versioned device API. Device authentication is
not interchangeable with a browser cookie session. The API will:

- pair and revoke devices;
- authorize narrowly scoped device principals;
- return candidates using the canonical TypeScript catalog and reception
  models;
- accept idempotent, explicit logbook and wishlist mutations;
- keep live tuner state ephemeral;
- use a hibernating Durable Object only when browser/Bridge live relay is added.

D1 will store device identity, credential digests, bounded security events, and
explicit log snapshots. It will not store continuous VFO, RDS, signal, FFT, IQ,
or waterfall telemetry. R2 remains limited to deliberately uploaded audio
evidence under the existing quota controls.

## Protocol invariants

- Protocol versions are independent of application versions.
- Frequency and bandwidth use integer hertz.
- Timestamps use UTC ISO 8601.
- Every measurement includes its source, unit, and calibration status.
- SDR# `VisualPeak`, `VisualFloor`, and `VisualSNR` are relative display metrics;
  they are not labelled dBm, dBuV, or dBuV/m without calibration.
- A full snapshot is sent after connection or reconnection.
- Partial updates include a monotonic sequence number.
- Durable mutations use a client UUID and server-side idempotency.
- Commands have a UUID, expected state revision, expiry, and ACK/NACK.
- Unknown additive fields are tolerated within a protocol major version.

## Authentication decision

Pairing uses a browser-assisted device authorization flow:

1. The Bridge generates a device key pair and requests a one-time device code.
2. It displays a short user code and opens DXNexus in the browser.
3. The authenticated user reviews device identity, fingerprint, and scopes.
4. Approval grants a short-lived access credential and a rotating renewable
   credential bound to the device key.
5. The renewable credential is protected locally by DPAPI; only its digest is
   stored server-side.
6. Account > Connected Devices can revoke the device and its live sessions.

Initial scopes:

```text
sdr:state:write
sdr:context:read
logbook:create
wishlist:write
```

Future control scopes remain absent and disabled by default:

```text
sdr:tune
sdr:mode
sdr:bandwidth
audio:capture
spectrum:publish
```

## First useful release

The first release is read-only with explicit mutations:

1. Observe SDR# frequency, mode, bandwidth, playback, and optional RDS.
2. Select a DXNexus receiver profile and listening point.
3. Request canonical candidates after the tuner remains stable.
4. Display station, frequency, schedule, distance, bearing, reception tier,
   model confidence, received status, and wishlist status.
5. Open the selected station in DXNexus.
6. Create a log only after explicit user confirmation.
7. Queue an explicit log or wishlist mutation while offline and deliver it
   exactly once after reconnection.

Remote tuning, DSP hooks, audio capture, and spectrum overlays are excluded from
the first release.

## Privacy defaults

Sent when integration is enabled:

- tuned and center frequency;
- detector/mode and bandwidth;
- playback state;
- compatibility versions;
- selected DXNexus listening-point identifier.

Optional and independently controllable:

- RDS PI/PS/RT;
- relative display metrics.

Never sent automatically:

- raw IQ;
- audio;
- FFT or waterfall;
- Airspy serial number;
- Windows account or computer name;
- process list, files, GPS, or crash dumps.

Candidate requests are stateless and are not persisted as listening history.

## Compatibility and distribution

- The first compatibility target is the current production SDR# SDK/revision.
- x86 and x64 are tested independently.
- Beta SDR# revisions are compatibility targets, not release dependencies.
- The plugin should be managed `AnyCPU` unless testing proves a host-specific
  target is required.
- SDR# host assemblies are referenced from a developer-supplied SDK directory
  with copy-local disabled.
- Releases contain only SDRNexus binaries and dependencies.
- Airspy/SDR# binaries and trademarks are not redistributed.
- Stable and beta channels use signed manifests, SHA-256, atomic replacement,
  and rollback.

## Consequences

### Benefits

- Network and authentication failures cannot directly destabilize SDR#.
- The plugin contains no reusable cloud credential.
- Bridge releases can evolve independently from the loaded SDR# DLL.
- Offline logging, reconnection, and future multi-SDR support share one client.
- The canonical DXNexus scientific model remains server-side.
- A future browser companion can reuse the same live relay.

### Costs

- Installation includes both a plugin and a per-user Bridge.
- Plugin/Bridge protocol compatibility must be tested.
- Pairing and updates require additional infrastructure.
- Two Windows processes require coordinated diagnostics and version reporting.

These costs are accepted because SDR# stability and credential isolation are
more important than minimizing the number of binaries.

## Rejected alternatives

### Direct network client inside the SDR# plugin

Rejected because TLS, authentication, retry, cache, and offline storage would
run inside the SDR# process and enlarge the failure and credential boundary.

### Browser connecting to a localhost HTTP/WebSocket server

Rejected because it introduces local ports, firewall/CORS/CSP/mixed-content
issues, browser-to-localhost attack surface, and harder device discovery.

### Reimplementing DXNexus ranking in C#

Rejected because the plugin and website would drift scientifically and could
rank the same frequency differently.

### Continuous cloud spectrum or IQ streaming

Rejected due to privacy, bandwidth, cost, and DSP stability. Spectrum analysis
will remain local unless a later explicit feature requires compact derived data.

## Validation gates

Before the architecture is considered production-ready:

- 60-minute SDR# soak with continuous tuning and no audio underruns;
- less than 1% additional idle CPU in the SDR# process;
- no network or unbounded allocation on UI/DSP paths;
- Named Pipe rejects a different Windows user;
- device tokens cannot access browser/admin endpoints;
- revocation terminates subsequent API and live access;
- candidate output matches the DXNexus website for golden cases;
- offline mutations synchronize exactly once;
- no signal reading is silently relabelled as a calibrated unit;
- x86/x64 and supported SDR# revisions are recorded in a compatibility matrix.

## Implementation sequence

1. Create and approve this ADR.
2. Define the versioned protocol schemas.
3. Scaffold the C# solution and compile the official empty plugin template.
4. Implement the SDR# host adapter and fake host tests.
5. Implement the Named Pipe and local Bridge state flow.
6. Implement DXNexus device pairing and Connected Devices.
7. Extract a server-safe candidate service from DXNexus.
8. Deliver the read-only candidates MVP.
9. Add idempotent logbook and wishlist mutations with offline delivery.
10. Add the optional browser live companion.
11. Add remote control only behind a per-session local opt-in.
12. Prototype the visible-spectrum overlay using public SDK APIs.

