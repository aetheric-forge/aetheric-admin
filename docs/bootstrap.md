# Platform bootstrap in admin

Admin consumes the shared `aetheric-web-components` repository through the pinned `web-components` submodule. The Keycloak connection, administrator creation/existing-account selection, administrator sign-in, infrastructure tests, and encrypted credential save are the same library workflow used by the provisioner.

In production, admin checks the five root credentials needed for University provisioning before registering operational services. If any are missing, it automatically starts the existing bootstrap workflow and redirects `/university` to `/setup`. `Bootstrap:AutoSetup=false` disables that automatic behavior; development defaults to disabled.

Set `BootstrapConnection:PublicOrigin` to the deployment's public HTTPS origin (or `Admin:PublicOrigin` as a fallback). Setup authority, realm, client ID and administrator role can be set through `BootstrapConnection`; otherwise existing bootstrap state is authoritative, with normal Keycloak settings used for a new deployment. Use a provisioner client with the realm administration permissions required by the existing workflow, and register its `/setup/signin-oidc` callback.

New automatic setup initializes deployment-bound registry state once, preserves existing state and encrypted credentials, and requires administrator verification and connection tests. When legacy credentials exist without infrastructure progress, they are offered for retesting under the selected verified administrator. Completed or corrupt state is never reset. After saving all tested connections, the completion page reopens the host in normal mode and sends the browser to `/university`; no deployment-mode toggle is required. Normal admin authentication still applies.

An explicit deployment mode remains available (`Bootstrap:Enabled=true`). It does not register the operational Redis/Mongo clients, Campus services, maintenance scheduler, or normal admin routes. Normal mode retains its existing authentication and institution-specific configuration. Explicit bootstrap mode remains active until the deployment configuration is changed.

## Start locally

Initialize submodules first:

```sh
git submodule update --init --recursive
```

Configure environment variables (the initial client secret is entered in the browser, not stored here):

```sh
export ASPNETCORE_ENVIRONMENT=Development
export Bootstrap__Enabled=true
export BootstrapConnection__Authority=https://sso.aethericforge.ca
export BootstrapConnection__Realm=int.aethericforge.ca
export BootstrapConnection__ClientId=provisioner
export BootstrapConnection__PublicOrigin=http://127.0.0.1:5180
# Prefer absolute persistent paths so working-directory changes cannot select another deployment.
export BootstrapConnection__StateDirectory=/absolute/private/admin-bootstrap/state
export Bootstrap__ProtectionKeyDirectory=/absolute/private/admin-bootstrap/protection
export RootCredentials__Directory=/absolute/private/admin-bootstrap/credentials
export RootCredentials__KeyDirectory=/absolute/private/admin-bootstrap/root-key
```

Use the actual initial OIDC client ID and realm for your deployment. That client must permit the callback `http://127.0.0.1:5180/setup/signin-oidc` (or the configured HTTPS public origin plus `/setup/signin-oidc`).

Initialize **once for a new deployment**, then run:

```sh
dotnet run --project src/AethericAdmin.Web --no-launch-profile -- --initialize-bootstrap
dotnet run --project src/AethericAdmin.Web --no-launch-profile --urls http://127.0.0.1:5180
```

Visit `http://127.0.0.1:5180/setup`. Connect the initial realm-admin client, create or select the Forge administrator, sign in as that account, then test and save Redis, RabbitMQ, Postgres and MongoDB credentials. Mongo includes authentication database and direct connection; RabbitMQ accepts an HTTPS management API URL. The completion screen ends this phase.

Initialization refuses to overwrite existing state. Ordinary startup refuses missing, corrupt, or mismatched deployment state. A completed deployment stays closed; changing `Bootstrap:Enabled` does not reset it. The library's 20-minute setup session and ten-minute connection-test receipts are preserved. Reconnect and sign in as the selected administrator to resume incomplete infrastructure setup.

## Container

`compose.bootstrap.yaml` runs admin on Vulcan's host network, listening on loopback port 5180 for the host reverse proxy. Stop the old standalone provisioner if it occupies that port. Configure `KEYCLOAK_AUTHORITY`, `KEYCLOAK_REALM`, `KEYCLOAK_CLIENT_ID`, and `ADMIN_PUBLIC_ORIGIN` in the shell or a private `.env`. Production expects an HTTPS public origin. Set `ASPNETCORE_ENVIRONMENT=Development` explicitly for local loopback HTTP testing.

```sh
docker compose -f compose.bootstrap.yaml build
# New deployment only:
docker compose -f compose.bootstrap.yaml run --rm admin --initialize-bootstrap
docker compose -f compose.bootstrap.yaml up -d
```

The four named volumes keep bootstrap state, Data Protection keys, encrypted root credentials, and their encryption key separate. Back them up together. The existing public internal CA is trusted in both image stages; credentials and local state are excluded from the build context.

To resume the earlier provisioner deployment, transfer its bootstrap state, encrypted credential files and ORIGINAL root encryption key into the corresponding admin volumes, preserving permissions, before starting admin. Do not initialize again. Realm, client ID and administrator role must match the saved deployment. Browser sessions need a fresh login because admin uses a separate Data Protection application name. Completed credentials do not automatically become institution credentials for normal admin mode.

After this phase, disable `Bootstrap:Enabled` and restart only when normal admin's existing institution-specific configuration is ready. Root credentials are retained for the runtime Provisioner; this integration does not provision child resources or configure the subscriber.

## Dependency and verification notes

The runtime pin is `fe4dcc5a548e7009f75442c41a6a7003b6092ddd` (bootstrap v1 interoperability profile and completion identity preservation). `Directory.Build.targets` makes provisioning's Registry adapter reference this same runtime, avoiding a second assembly copy through nested submodules. Membership also resolves the root primitives copy to avoid losing duplicate project dependencies from the published manifest. Its internal HTTP-handler test seam is exposed to `AethericAdmin.Tests` for controlled Keycloak integration fixtures. No submodule source changes are required.

`AethericAdmin.BootstrapShell` only owns the bootstrap HTML document/router. A separate assembly lets endpoint discovery exclude operational admin pages completely. Workflow pages and assets come from `aetheric-web-components`, not copied Razor files.

Run `dotnet test --configuration Release` with the existing isolated Redis fixture. `REDIS_TEST_HOST`, `REDIS_TEST_PORT`, and `REDIS_TEST_PASSWORD` select that fixture. Bootstrap tests use local mock OIDC/Keycloak responses and a fake connection validator; live infrastructure validation remains supplied by the tested provisioner implementation. Claude owns the runtime subscriber work.

CI also publishes admin and runs `scripts/smoke-bootstrap.py` against the published output, verifying first startup, static assets, and route isolation without live Keycloak/Redis/Mongo dependencies.

The web-components repository is public. Normal recursive submodule checkout and the default GitHub Actions token are sufficient; no cross-repository secret is required.

## Admin login credentials after bootstrap

Admin opts into saving the provisioner OIDC client secret in the existing encrypted root
credential store under `provisioner-client`, after protocol validation and sign-in as the
selected administrator. The record binds the secret to its issuer and client ID. Normal
mode loads it only for that same client and uses the registered `/setup/signin-oidc` callback.
Keep the root-credentials and root-key volumes together across mode changes and restarts.

For deployments completed before this change, restart in bootstrap mode and open `/setup`.
Reconnect the existing provisioner client and sign in as the selected administrator. Only
missing-secret recovery is permitted after completion; infrastructure completion and saved
credentials remain intact. Then restart normal mode with `ASPNETCORE_ENVIRONMENT=Production`.
Do not initialize bootstrap again. Without a matching secret, normal mode serves an HTTP 503
setup-required page instead of making an OIDC request that fails. An explicit
`Keycloak:ClientSecret` still takes precedence.

### RabbitMQ access before campus submission

On `/university`, use **RabbitMQ access** before **Submit to Operations**:

- **Operations broker (AMQP)**: enter the messaging host, port (5672, or 5671 with TLS), virtual host, username and password. Match the broker and virtual host used by the Operations worker. Save the Operations connection.
- **Campus provisioning (management API)**: enter the HTTP/HTTPS management base URL (typically port 15672), administrator username and password. Save the provisioning credentials. Operations uses this account to create campus virtual hosts and users.

These connections may use different hosts and accounts. Both are encrypted in the existing root-credential volume, using its retained encryption key. AMQP settings use the separate `rabbitmq-amqp` entry and take precedence over the app's RabbitMQ configuration; management credentials continue to use `rabbitmq`. Passwords are not reloaded into inputs or included in draft downloads. Enter a password when saving a replacement connection.

The sender reloads saved AMQP settings without an app restart and restores result subscriptions after connection changes. Missing or unavailable RabbitMQ connections leave the setup UI accessible; subscription attempts retry in the background. Saving credentials does not test access or create resources. Submission still requires the other provisioning credentials (MongoDB, Keycloak, S3 and Redis).

Automatic startup and routing are smoke-tested by `scripts/smoke-auto-setup.py` against the published app, including state preservation across restarts.
