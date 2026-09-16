# Redis persistence in admin

Admin stores Workbench values (including Maintenance run history) in Redis and uses Redis for its ASP.NET Core Data Protection key ring. Job definitions and encrypted SSH credentials remain in MongoDB. The runtime and shared contract submodules are unchanged.

## Configuration

Set these environment variables through the deployment's secret/configuration mechanism:

```text
Redis__Host=redis-host
Redis__Port=6379
Redis__Ssl=true
Redis__Database=0
Workbench__Redis__User=admin-workbench
Workbench__Redis__Password=<secret>
ForgeCampus__Redis__User=admin-key-ring
ForgeCampus__Redis__Password=<secret>
```

Endpoint settings can be overridden under each institution's `Redis` section. Passwords must be supplied locally to Workbench and ForgeCampus; the configuration resolver never inherits platform credentials. `User` is optional for password-only Redis. Admin does not create servers, ACL users, or passwords.

The checked-in defaults use:

- `Workbench:KeyPrefix`: `aetheric-admin:workbench:v1:`
- `DataProtection:Key`: `aetheric-admin:data-protection:keys`
- `DataProtection:ApplicationName`: `AethericAdmin`

Keep these values and the selected Redis databases stable across restarts. Give environments separate prefixes/key names or databases. Use separate ACL users scoped to the relevant key spaces. Local development uses `localhost:6379`, TLS disabled, and the test password `dev`; deployment credentials must override those values.

No in-memory fallback is used when Redis is missing, unreachable, or rejects credentials. Malformed stored Workbench data is an error, not an empty ledger. Workbench keys include a hash of the value type and string key; arbitrary object keys are intentionally unsupported. Values have no TTL. Subscription notifications remain local to the writing process.

## Operational requirements

Run **one active admin instance**. The runtime Caretaker still serializes its ledger's read/modify/write operations with a process-local semaphore. Redis persistence does not make that ledger or the cron scheduler safe for concurrent replicas. A collected command with no outcome remains visible after a crash; this change does not automatically replay external SSH/code jobs.

Use Redis persistence (AOF or a suitably configured managed equivalent), persistent storage, backups, and a non-evicting policy. Losing the key ring makes MongoDB's protected SSH credentials unreadable and invalidates protected cookies. Workbench history and key-ring entries must not be treated as disposable cache data. Back up the ring together with the encrypted credentials it protects.

Redis key-ring persistence does not itself encrypt the Data Protection keys at rest. Restrict Redis access and protect its storage/backups; configure an external key-wrapping mechanism if deployment policy requires it. Microsoft documents these requirements in [Data Protection key storage providers](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers?view=aspnetcore-10.0#redis).

## Existing deployments

In-memory run history cannot be recovered after the old process exits. The old `aetheric-web` job domain is corrected to `aetheric-admin`, which is what the Maintenance review reads.

Before switching an existing deployment, retain its old Data Protection key ring and application discriminator. If it already holds encrypted SSH credentials, migrate those keys into the configured Redis repository and preserve its old application discriminator via `DataProtection:ApplicationName`, or re-enter the credentials using the new ring. This change does not automatically import filesystem keys or decrypt old credentials with a new ring. The protector purpose for SSH credentials stays `AethericAdmin.Web.Maintenance.SshCredentials`.

## Verification

```sh
git submodule update --init --recursive
dotnet build --configuration Release
# Redis test server must require the supplied password; defaults are localhost:6379 / dev.
REDIS_TEST_HOST=127.0.0.1 REDIS_TEST_PORT=6379 REDIS_TEST_PASSWORD=dev dotnet test --configuration Release
# Configuration-only checks without Redis:
dotnet test --configuration Release --filter 'Category!=RedisIntegration'
```

Integration tests use unique prefixes and delete only their own keys. They verify completed history and credential decryption across separate .NET processes, non-expiring data, type separation, local subscriptions, cancellation before writes, corrupted-ledger rejection, failed authentication, and Data Protection application isolation. These tests do not execute SSH/code jobs or use production MongoDB/Redis.

Verified locally with Redis 7.4.11: all eight tests passed. A separate write → Redis container restart with AOF enabled → fresh-process read check also recovered the completed run and decrypted the synthetic credential successfully.
