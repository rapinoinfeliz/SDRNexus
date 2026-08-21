# DXNexus refresh-token rotation handoff

## Observed production failure

On 2026-08-21 the paired Windows Bridge successfully wrote a refreshed device
credential at 15:24:34 UTC. The returned access credential expired five minutes
later, at 15:29:34 UTC. Before or at the next authenticated request, production
returned `Refresh credential is invalid or expired` from
`POST /api/sdr/v1/token/refresh`.

Only non-secret metadata was inspected. The access and refresh values remained
64 characters, the device id stayed stable, and the credential stayed protected
by Windows DPAPI. No token value was logged or copied into this repository.

This sequence is consistent with a refresh-token rotation problem: generation
N succeeds and is saved by the Bridge, but generation N+1 is rejected. A server
deployment or explicit device revocation can produce the same one-time symptom,
so the server-side checks below must distinguish the two cases.

## Server checks required in DXNexus

1. Pair a fresh device and retain only token digests server-side.
2. Refresh generation N and assert that the digest of the newly returned refresh
   token replaces the prior digest in the same durable transaction.
3. Use the returned token to refresh generation N+1 after the five-minute access
   token expires; it must succeed once.
4. Replay generation N after rotation; it must fail without revoking generation
   N+1 unless the product intentionally implements refresh-family reuse
   detection.
5. Verify the DPoP public-key thumbprint and `nonce` validation use the new
   refresh credential received in the request and remain bound to the same
   device id.
6. Run the test across a Worker deployment to ensure no in-memory token state is
   required and the D1 update is committed before the response is returned.

## Windows recovery behavior

The SDR# panel now exposes **Reconnect DXNexus** for authentication failures.
Successful pairing reloads the newly saved DPAPI credential into the running
Bridge, resets candidate state, and retries the current frequency without
requiring a Bridge restart. This improves recovery but does not mask a broken
server rotation: the two-consecutive-refresh server test above remains required.
