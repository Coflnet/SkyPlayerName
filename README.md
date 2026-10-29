## Purpose
It turned out that retrieving a playername via uuid is a bottleneck. 
This service attempts to remove that.

## Deploying
This project should be deployed within a container. 
### Configuration
See appsettings.json  
This service requires read access to the main db.

### TLS and connection readiness

TLS is opt-in. Keep the existing `DBConnection` (including every host, username,
password and pool option). `DB:TLS:CA_CERT_PATH`, `DB:TLS:CERT_PATH` and
`DB:TLS:KEY_PATH` enable verified mutual TLS with a root PEM bundle and client
fullchain/key files. Each database host must match its server certificate's TLS
identity. No hostname or certificate errors are accepted.

MySqlConnector 2.6.2 can accept native `VerifyFull` certificate errors when MariaDB
supplies a password-bound TLS fingerprint. To enforce the configured CA and
hostname policy, this service instead uses the driver's supported `Required`
mode with **unconditional strict Coflnet.Core validation before authentication**.
The native `SslCa` field is cleared so it cannot override that callback; the
shared PEM owner reads `CA_CERT_PATH` itself. Both synchronous and asynchronous
EF connection opens install the validator and register only validated public
client intermediates in the workload user's CA store. Provide a private,
writable, ephemeral `HOME`; no root or machine trust store is changed.

New physical connections reread certificate/key/CA files. TLS connections have a
maximum pool lifetime of 300 seconds, preserving any explicitly shorter lifetime.
Existing connections finish normally; publish PEM pairs atomically. Database
connection timeout is capped at 10 seconds, including when TLS is disabled.

`HTTPS:TLS` accepts the same file keys for the server fullchain/key and adds
native HTTPS on `8443` while retaining HTTP on `8000`. Client certificates are
requested and validated when presented. `REQUIRE_CLIENT_CERTIFICATE` defaults to
`false`; set `HTTPS:TLS:REQUIRE_CLIENT_CERTIFICATE=true` for a later HTTPS-only
client enforcement phase. New handshakes load renewed server certificates.

A required, read-only `SELECT 1` runs before the HTTP server starts, with a
10-second deadline. `/health/ready` repeats the actual database check with a
5-second deadline and returns `503` on failure. Sensitive EF data logging is
disabled. Existing API routes and authentication are unchanged.

The Docker build runs `Tests/SkyPlayerName.Tests.csproj` excluding `Integration`.
A required CI job runs the real MySQL TLS integration test against a disposable,
digest-pinned official MariaDB container, before the existing shared container
build, mandatory scan and GitOps promotion. Local integration tests require
Docker; they create fresh data and synthetic certificates, bind only loopback,
and remove their exact container afterward. The existing public HypixelSkyblock
project reference must be available at `../dev/hypixel.csproj`, as in Dockerfile.

`HypixelSkyblock.commit` pins the shared project used by both the integration job
and image build. Update it only with the TLS wire tests passing.
