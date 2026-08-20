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

Architecture and protocol definition, with a buildable Windows solution
scaffold. No production plugin binary is available yet.

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

## Important boundaries

- No DXNexus password is stored by the plugin.
- No raw IQ, spectrum, waterfall, or audio is uploaded automatically.
- SDR# display readings are not represented as calibrated dBm or dBuV.
- SDR# and Airspy assemblies are not redistributed by this repository.
