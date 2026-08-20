# SDRNexus

SDRNexus is the Windows companion for DXNexus. It connects SDR# to the
DXNexus station catalog, reception tools, wishlist, and logbook without
placing network or authentication work inside the SDR# process.

The project is currently being implemented incrementally. The accepted
architecture is documented in
[`docs/adr/0001-plugin-bridge-architecture.md`](docs/adr/0001-plugin-bridge-architecture.md).

## Planned components

```text
SDR#
  -> DXNexus SDR# Plugin
  -> local named pipe
  -> DXNexus Bridge for Windows
  -> DXNexus HTTPS/WSS API
```

- **Plugin:** reads the current SDR# state and presents DXNexus context.
- **Bridge:** owns credentials, networking, offline delivery, and updates.
- **Contracts:** versioned messages shared with the DXNexus backend.

## Current status

Architecture, protocol, a buildable Windows solution, the bidirectional local
named-pipe transport, secure browser-assisted device pairing, rotating DPoP
credentials, exact-frequency station candidates, offline-safe explicit actions,
and the optional live browser companion are implemented. Release artifacts are
test packages until the real SDR#/Airspy compatibility gates are completed.

## Pairing the Bridge

1. Start **DXNexus Bridge** on Windows.
2. From its tray menu choose **Connect to DXNexus…**.
3. The Bridge creates a device-only P-256 key and opens DXNexus with a
   ten-minute code.
4. Sign in and approve it under **Account → Connected devices**.
5. The private key and device credentials are encrypted for the current
   Windows user with DPAPI. The SDR# plugin never receives a DXNexus password
   or cloud token.

After pairing, tune SDR# to an FM, AM/MW, or SW broadcast channel. The Bridge
waits briefly for tuning to settle, uses the default Listening Point and
receiver saved in DXNexus, and returns the ranked exact-frequency candidates
to the plugin. The panel shows distance, bearing, modeled field strength, and
received/wishlist state. **Target** updates Want to hear, while **Log** opens a
small explicit confirmation form and saves the reception with the current SDR
snapshot and selected setup. Credentials remain inside the Bridge and no log
is created without a user action.

Explicit log and wishlist mutations use client-generated idempotency IDs. If
DXNexus is temporarily unavailable, the Bridge stores them in a per-user local
SQLite queue and retries with bounded exponential backoff. Transient tuner,
signal and RDS snapshots are never queued as listening history.

The tray option **Live browser companion** is off by default and remembered per
Windows user. When enabled, Account → Connected devices shows the current SDR#
frequency and up to six exact-channel candidates through a private outgoing
WebSocket. The relay is ephemeral: it does not store VFO state in D1/R2 and does
not transmit audio, IQ, FFT, waterfall, hardware serials, or listening history.

Browser tuning remains independently locked. It is available only after the
user selects **Allow browser tuning for 15 minutes…** in the local Bridge tray
and confirms the warning. Every tune command expires after 15 seconds, must
match the current tuner sequence/frequency, is revalidated inside the SDR#
plugin, and is acknowledged back to the browser. The permission is never
persisted and returns to locked when the Bridge restarts or the timer expires.

## Development prerequisites

- .NET 9 SDK;
- Node.js 22 for contract validation;
- the pinned official SDR# SDK reference assemblies.

Install only the required SDK reference assemblies into the ignored `.sdk`
directory:

```bash
./scripts/fetch-sdrsharp-sdk.sh
```

Then build:

```bash
dotnet build SDRNexus.sln
npm test
```

## Windows test package

Every successful `.NET` workflow produces a 14-day `SDRNexus-windows-x64`
artifact. Extract it, open PowerShell in the extracted directory, and run:

```powershell
.\install.ps1 -SdrSharpPath "C:\path\to\sdrsharp-x86"
```

The installer reads `core.pluginsDirectory` from `SDRSharp.config`, copies only
the four SDRNexus plugin assemblies there, installs the per-user Bridge under
`LocalAppData\Programs`, and optionally starts it with Windows. SDR# must be
closed. The package contains SHA-256 checksums and never redistributes SDR# or
Airspy binaries. This remains a test package until the compatibility gates are
completed on Windows with real SDR# hardware.

## Important boundaries

- No DXNexus password is stored by the plugin.
- No raw IQ, spectrum, waterfall, or audio is uploaded automatically.
- SDR# display readings are not represented as calibrated dBm or dBuV.
- SDR# and Airspy assemblies are not redistributed by this repository.
