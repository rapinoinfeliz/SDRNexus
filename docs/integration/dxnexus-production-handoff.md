# DXNexus production integration handoff

Status date: 2026-08-20 (DXNexus server fix deployed; Windows acceptance pending)
Validated SDRNexus client revision: `4cdee24515b728287009e0d7d782d24a0c6054bf`

## Resolution update

DXNexus commit `55dc845310b039a2e1fb8db24acbbbb05e1582c8` was deployed successfully by
GitHub Actions run `32434932427` on 2026-08-20. No SDRNexus contract or Windows
client change was required for this server-side repair.

The deployment:

- explicitly binds the published static catalog to the Worker as `ASSETS`;
- falls back to a same-origin catalog request if an older generated deployment
  omits that binding;
- proves known-frequency and empty-frequency behavior with automated tests;
- uses one shared browser live client across the DXNexus UI;
- adds **Tune SDR#** to Station Details and the station action dial;
- sends one short-lived command based on a fresh tuner state;
- displays pending, confirmation, rejection, timeout and disconnect states;
- requires both `live.command.result` success and a subsequent tuner state at
  the requested frequency before displaying **Tuned**; and
- routes command results only to the browser connection that initiated them.

DXNexus validation passed: lint, TypeScript, production build, 237 web tests,
the radio-catalog publication audit and the post-deployment verification. The
published catalog manifest is readable and reports 66,114 FM sites, 11,451 AM
sites and 464 SW sites. The authenticated candidate call and physical tuner
change must now be repeated on Windows because the DPoP device private key and
local SDR# process correctly remain on that machine.

### Short Windows retest

1. Pulling or reinstalling SDRNexus is not required; keep the currently tested
   Bridge/plugin build.
2. Start SDR#, then start SDRNexus Bridge and wait for **Plugin connected** and
   **DXNexus cloud connected**.
3. Tune manually to 101.7 MHz WFM. The candidate request should return HTTP 200
   and no longer show `catalog-unavailable`.
4. Enable **Live browser companion** and unlock browser tuning for 15 minutes.
5. Open the production DXNexus site, select a different FM station and press
   **SDR#** in Station Details (or **Tune SDR#** in the right-click dial).
6. Confirm the UI progresses through **Tuning…** to **Tuned** and SDR# changes
   to the exact station frequency.
7. Lock browser tuning locally and repeat once; the browser must show the
   Bridge rejection instead of silently claiming success.

If step 3 still fails, save the new sanitized request ID and response body. If
step 6 fails, save the command ID plus the Bridge/plugin status text; do not
share tokens, DPoP keys or account cookies.

This document is the handoff to the DXNexus web/Worker repository. The Windows
plugin and Bridge are installed and working locally, but the production DXNexus
side still prevents the complete user flow.

## User-visible failures

1. The SDR# panel shows:
   `DXNexus cloud degraded · The station catalog is not available`.
2. No station candidates appear in the SDR# panel.
3. Selecting another station in the signed-in DXNexus web application does not
   change the SDR# frequency, even while the panel reports both
   `Live browser connected` and `Browser tuning enabled`.

Treat the catalog lookup and browser tuning as two independent production
flows. Both must pass their acceptance tests before closing this handoff.

## Evidence already collected on Windows

- SDR# revision 1921 loads the DXNexus plugin without a DXNexus load error.
- The plugin-to-Bridge named pipe is connected.
- A synthetic `command.tune` sent through the local named pipe changed SDR#
  from 103.5 MHz to 103.6 MHz and restored it to 103.5 MHz successfully.
- Device authentication is accepted by production.
- `GET /api/sdr/v1/setup` succeeds for the paired device and returns the saved
  listening points and receiver profiles.
- The authenticated Bridge handshake to
  `wss://dxnexus.rapinoinfeliz.workers.dev/api/sdr/v1/live/bridge` succeeds.
- The running plugin reports `Live browser connected`.
- An authenticated candidate request for 101.7 MHz/WFM/200 kHz returns HTTP
  503 with:

  ```json
  {
    "error": "The station catalog is not available",
    "code": "catalog-unavailable"
  }
  ```

- All SDRNexus tests pass: 19 .NET tests plus validation of 12 schemas, 8
  examples and OpenAPI 1.0.0.

Do not change the Windows plugin to manufacture station data or bypass the
relay. The catalog model and authenticated browser session belong on the
DXNexus side of the boundary.

## Contract source of truth

The DXNexus implementation must consume or mirror the files under
`contracts/sdr/v1` from the validated SDRNexus revision above:

- `openapi.json`
- `candidate-request.schema.json`
- `candidate-response.schema.json`
- `live-state.schema.json`
- `live-command.schema.json`
- `problem.schema.json`
- all referenced definitions in `common.schema.json`

Run `npm test` in SDRNexus after any contract edit. If the DXNexus repository
needs a protocol change, update both repositories, add compatible tests and
publish the SDRNexus commit before deploying the incompatible server change.

## Work required in the DXNexus repository

### 1. Restore the production station catalog

Inspect the production Cloudflare Worker configuration, environment bindings,
catalog build/import step, D1/R2/KV bindings and deployment artifacts used by
`POST /api/sdr/v1/candidates`.

The handler must:

1. authenticate the device principal and DPoP proof;
2. validate that `receiver.listeningPointId` and
   `receiver.receiverProfileId` belong to that device's user;
3. query the canonical station catalog for the exact `frequencyHz` and band;
4. apply the listening point/receiver reception context;
5. return HTTP 200 conforming to `candidate-response.schema.json`, including a
   valid `catalogVersion`, `modelVersion`, and zero or more candidates;
6. return a problem document conforming to `problem.schema.json` for genuine
   failures, including `requestId` and `retryAfterSeconds` when appropriate.

An empty exact-frequency result is a successful HTTP 200 response with
`candidates: []`; it must not be reported as `catalog-unavailable`.

Do not merely catch the 503 or replace it with an empty response. Prove that
the deployed Worker can read the real production catalog and can return a
known station on a known populated frequency.

### 2. Complete the private browser live relay

Verify the browser/session WebSocket route paired with `/live/bridge` and the
Durable Object or equivalent relay used in production.

The relay must:

- associate a device-authenticated Bridge socket with the correct `userId`
  and `deviceId`;
- associate the signed-in browser socket with the same user and selected
  device;
- forward `live.state` only from that Bridge to authorized browser sessions;
- forward `live.command.tune` only from an authorized browser session to the
  selected Bridge;
- forward the resulting `live.command.result` back to the initiating browser;
- remove stale sockets and device mappings on close/reconnect;
- keep tuner state ephemeral rather than persisting it to D1/R2;
- reject cross-user and wrong-device routing.

Add structured, privacy-safe logs for socket connect/disconnect, selected
device, message type, command ID, relay outcome and rejection code. Do not log
tokens, DPoP material, precise user location, audio, IQ or waterfall data.

### 3. Send a correct tune command from the web application

In **Account -> Connected devices**, do not enable tuning until a fresh
`live.state` has been received for the selected device. When the user chooses
a station/frequency, send exactly one message conforming to
`live-command.schema.json`:

```json
{
  "type": "live.command.tune",
  "protocol": "1.0",
  "commandId": "<new UUID>",
  "deviceId": "<selected connected device UUID>",
  "frequencyHz": 98900000,
  "expectedFrequencyHz": 92300000,
  "expectedSequence": 42,
  "expiresAt": "<UTC timestamp no more than 15 seconds in the future>"
}
```

Field rules:

- `frequencyHz` is the target station frequency, as integer hertz.
- `expectedFrequencyHz` is the current SDR# frequency from the most recent
  `live.state.snapshot.radio.frequencyHz`, not the target frequency.
- `expectedSequence` is the corresponding most recent snapshot sequence.
- `deviceId` is the device whose live state supplied those expected values.
- `expiresAt` must be short-lived. Use at most 15 seconds.
- Never reuse a `commandId`.

Prevent duplicate commands caused by nested click handlers, link navigation or
React development-mode effects. Disable or show a pending state for that
device until `live.command.result`, timeout or disconnect.

Display the returned `live.command.result.message` to the user. A success must
also be confirmed by the next `live.state` reporting the requested frequency.

### 4. Preserve current safety behavior

The Bridge intentionally rejects a command when:

- the local 15-minute tuning permission is locked or expired;
- the command has expired;
- its `deviceId` targets another Bridge;
- `expectedFrequencyHz` no longer matches the SDR# tuner;
- `expectedSequence` is newer than the current tuner sequence;
- the frequency is outside supported bounds; or
- the plugin is disconnected.

Signal-only snapshots may advance the current sequence. The Bridge therefore
accepts a command whose expected sequence is current or older, provided the
expected frequency still matches. The web implementation must use the latest
state but must not assume strict sequence equality on the server side.

## Required automated tests in DXNexus

Add tests at the lowest useful layers and at least one deployed/staging
integration test for each flow.

### Candidate tests

- authorized known frequency returns 200 and schema-valid candidates;
- authorized unknown exact frequency returns 200 with `candidates: []`;
- missing catalog binding/import fails deployment or health checking rather
  than silently deploying a permanently unavailable catalog;
- listening point or receiver owned by another user is rejected;
- response echoes the request ID, sequence and frequency correctly.

### Relay and web tests

- browser receives `live.state` only for its selected, owned device;
- tune click produces all required fields with integer hertz;
- command is delivered to the matching Bridge socket once;
- wrong-user and wrong-device commands are rejected;
- `live.command.result` returns to the initiating browser;
- reconnect replaces stale socket mappings;
- UI reports success, rejection, timeout and disconnection distinctly.

## Production acceptance procedure

Use a real signed-in browser and the paired Windows Bridge. Record sanitized
request IDs and command IDs in the test report.

1. Deploy the DXNexus Worker/web changes.
2. Confirm the authenticated setup request returns 200.
3. Tune SDR# to a known populated broadcast frequency.
4. Confirm `POST /api/sdr/v1/candidates` returns 200 and the plugin lists the
   expected candidate. The orange `catalog-unavailable` message must disappear.
5. Open **Account -> Connected devices**, select the paired Bridge and confirm
   its displayed frequency follows manual SDR# changes.
6. In the SDR# panel, enable browser tuning and accept the local warning.
7. Select a different station in DXNexus.
8. Confirm SDR# changes to the exact selected frequency within five seconds.
9. Confirm the browser receives a successful `live.command.result` and the next
   `live.state` reports the target frequency.
10. Let the local permission expire or restart the Bridge and confirm a new web
    tune is rejected visibly rather than silently.

## Completion handoff back to Windows

Commit and push all DXNexus changes, deploy them, and report:

- DXNexus repository commit hash;
- production deployment/version identifier;
- catalog health/test result;
- WebSocket relay integration-test result;
- one sanitized successful command/result trace;
- whether any SDRNexus contract or client change is still required.

If SDRNexus changes are required, open them against the repository containing
this document and reference this handoff. The Windows side will then pull,
build, test and reinstall the plugin and Bridge.
