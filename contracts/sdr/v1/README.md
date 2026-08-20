# SDRNexus protocol v1

This directory is the canonical machine-readable contract shared by:

- the SDR# plugin;
- the Windows Bridge;
- the DXNexus Worker API;
- the optional DXNexus live browser companion.

## Compatibility rules

- Frequencies and bandwidths are integer hertz.
- Times are ISO 8601 UTC strings.
- Measurements always declare source, unit, and calibration state.
- Additive fields are allowed within protocol major version 1.
- Consumers must ignore fields they do not understand.
- Removing, renaming, or changing the meaning of a field requires a new major
  protocol version.
- Durable mutations carry a client-generated UUID for idempotency.
- A relative SDR# display reading must not be relabelled as calibrated RF power
  or electric field strength.

## Schemas

- `radio-snapshot.schema.json`: complete state sent after connection or retune.
- `candidate-request.schema.json`: stateless DXNexus candidate query.
- `candidate-response.schema.json`: ranked candidates and scientific context.
- `pairing.schema.json`: browser-assisted device authorization messages.
- `logbook-create.schema.json`: explicit, idempotent quick-log mutation.
- `wishlist-mutation.schema.json`: explicit target add/remove mutation.
- `problem.schema.json`: stable API error representation.
- `pipe-envelope.schema.json`: framed Plugin/Bridge message envelope.

`openapi.json` documents the initial HTTPS API. Live WSS and remote-control
commands are intentionally excluded from the first release.

Run `npm test` at the repository root to compile every schema and validate all
examples.

