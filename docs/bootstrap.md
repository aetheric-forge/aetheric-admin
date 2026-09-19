# Platform bootstrap in admin

Admin consumes the shared `aetheric-web-components` repository through the pinned `web-components` submodule. The Keycloak connection, administrator creation/existing-account selection, administrator sign-in, infrastructure tests, and encrypted credential save are the same library workflow used by the provisioner.

Bootstrap is an explicit deployment mode (`Bootstrap:Enabled=true`). It does not register the operational Redis/Mongo clients, Campus services, maintenance scheduler, or normal admin routes. Normal mode retains its existing authentication and institution-specific configuration. There is no automatic switch to the dashboard or subscriber integration in this change.

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

The runtime pin is `6c55d21f486a349ad4beb671a4b72726bf2485bd` (main, campus post subscription). `Directory.Build.targets` makes provisioning's Registry adapter reference this same runtime, avoiding a second assembly copy through nested submodules. Membership also resolves the root primitives copy to avoid losing duplicate project dependencies from the published manifest. Its internal HTTP-handler test seam is exposed to `AethericAdmin.Tests` for controlled Keycloak integration fixtures. No submodule source changes are required.

`AethericAdmin.BootstrapShell` only owns the bootstrap HTML document/router. A separate assembly lets endpoint discovery exclude operational admin pages completely. Workflow pages and assets come from `aetheric-web-components`, not copied Razor files.

Run `dotnet test --configuration Release` with the existing isolated Redis fixture. `REDIS_TEST_HOST`, `REDIS_TEST_PORT`, and `REDIS_TEST_PASSWORD` select that fixture. Bootstrap tests use local mock OIDC/Keycloak responses and a fake connection validator; live infrastructure validation remains supplied by the tested provisioner implementation. Claude owns the runtime subscriber work.

CI also publishes admin and runs `scripts/smoke-bootstrap.py` against the published output, verifying first startup, static assets, and route isolation without live Keycloak/Redis/Mongo dependencies.

The web-components repository is currently private. Developer checkouts use its SSH URL. CI accepts `SUBMODULES_TOKEN`, a read-only token with access to admin and the private submodule repositories. The default workflow token only suffices if every submodule is publicly readable; configure the repository/organization secret before expecting a fresh CI checkout to succeed.
