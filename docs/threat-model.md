# Threat model

The source library, notes, URLs, metadata and agent output are untrusted. ProjectBrain performs no source execution, dependency install, engine invocation, shell command or external URL/model fetch. Static profiles are observations, not successful outcomes.

Controls implemented in source:

- Loopback Docker bindings, bearer credentials, fixed-time token comparison, explicit Host and Origin allowlists.
- Optional distinct agent credential blocks human REST writes; MCP has only read/proposal tools.
- Browser token stays in memory. No credential value in frontend build assets or request logs.
- Bounded request bodies, write rate limits, strict contracts, optimistic knowledge versions and durable retry receipts.
- Source mounts are read-only, API is non-root, capabilities are dropped and container filesystems are read-only except data/tmp.
- Every traversed/opened path is checked for containment and link/reparse-point ancestors before reading, and checked again after reading.
- Secret file exclusions, credential-line/token redaction, safe metadata parsing, XML DTD prohibition, binary metadata only.
- Atomic publication after complete traversal; root absence/cancellation/budget failures never imply removals.
- Escaped text rendering and restrictive production CSP; no HTML execution, automatic image loads or clickable unsafe URL schemes.
- Result evidence is checked against indexed paths/hashes; human approval is separate from successful test review.

Limits:

- This is a single-user local service. A process holding the primary human token can invoke human REST endpoints; tokens are not hardware-backed identities. Prefer the separate agent token.
- Path checks reduce link races but are not a kernel-enforced openat/no-follow filesystem sandbox. Do not grant hostile local processes write access to the mount concurrently. Docker's source mount prevents writes from this application, not from other host processes.
- Redaction is heuristic and cannot classify every arbitrary secret. Keep credential stores outside source mounts; raw human captures intentionally preserve original text in the private database.
- Reviewed evidence is a human assertion supported by recorded file hashes, not an independent execution attestation. The scanner never runs tests.
- One API instance per database. Remote access, multi-user RBAC, TLS/OAuth and hostile-network deployment require a separate design.
- The authored HTTP/browser/Compose suites have not run in this sandbox. These controls must not be represented as penetration-tested.

