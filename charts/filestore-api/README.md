# filestore-api chart

OCI chart for the FileStore API's own controller: `filestore-api`. Published from this repo (`ghcr.io/spydersoft-consulting/charts/filestore-api`), versioned alongside the container image.

This chart does **not** own or create any Kubernetes `Secret`, `ConfigMap`, or backing infrastructure (PostgreSQL, S3-compatible storage). It only references config/secrets **by name**, via `envFrom.secretRef`/`envFrom.configMapRef`, with the names themselves overridable values. Whoever composes this chart (today: `platform-helm-config`) is responsible for creating the referenced Secret/ConfigMap and for owning the backing Postgres instance.

## Values

- `controllers.filestore-api.containers.main.image.tag` — filestore-api image tag.
- `controllers.filestore-api.containers.main.envFrom` — supplied entirely by the caller; not defaulted here (every real caller overrides this in full to add its own `configMapRef`s — see the secrets contract below for what the secret must contain).
- `route.filestore-api.hostnames` — per-environment hostname(s); not defaulted here since every real caller supplies them.

## Secrets contract

The caller must create a secret named **`filestore-secrets`** containing:

- `ConnectionStrings__filestore` — PostgreSQL connection string (Npgsql format).
- `Storage__AccessKey` / `Storage__SecretKey` — S3-compatible object storage credentials.

The secret name is not hardcoded in this chart — it's supplied via the caller's `envFrom.secretRef.name` override, so a different composing repo could name/source it however it wants without any chart change.
