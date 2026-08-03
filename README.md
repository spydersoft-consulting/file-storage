# Spydersoft FileStore

Platform file storage service for Spydersoft applications. Provides two layers:

- **File Storage** (`/api/v1/filestore`): Raw blob storage with presigned URLs backed by S3-compatible storage (Garage)
- **Document Management** (`/api/v1/documents`): Named documents with versioning and retention policies

## Projects

- `Spydersoft.FileStore.Contracts` — Wire DTOs, enums, and client interfaces (NuGet)
- `Spydersoft.FileStore.Client` — HTTP client implementations (NuGet)
- `Spydersoft.FileStoreApi` — ASP.NET Core 10 API
- `Spydersoft.FileStore.AppHost` — .NET Aspire local development host

## Local Development

```powershell
dotnet run --project src/Spydersoft.FileStore.AppHost
```

Requires Docker. Starts PostgreSQL and MinIO containers.

## Consuming the Client

Register `Spydersoft.FileStore.Client` in a consuming app's DI container:

```csharp
builder.Services.AddSpydersoftFileStore(builder.Configuration);
```

This registers `IFileStoreClient` and `IDocumentClient` as typed `HttpClient`s, bound to a `FileStore`
configuration section:

```json
{
  "FileStore": {
    "BaseUrl": "https://filestore.example.com",
    "TokenEndpoint": "https://auth.example.com/connect/token",
    "ClientId": "your-service-client-id",
    "ClientSecret": "your-service-client-secret",
    "Scope": "filestore:read filestore:write"
  }
}
```

- `BaseUrl` is the only required value.
- `TokenEndpoint`/`ClientId`/`ClientSecret` are optional and opt the client into OAuth2
  client-credentials auth: a bearer token is fetched, cached, and refreshed automatically before
  it expires. Leave them unset for unauthenticated local/dev usage against an API that doesn't
  enforce `[Authorize]`.
- The client-credentials client needs to be registered with your identity provider with access to
  the `filestore:read` and/or `filestore:write` scopes, matching the policies `FileStoreApi`
  enforces on read vs. write endpoints.

## Codebase Conventions
- All C# classes sealed unless explicitly designed for inheritance
- Nullable reference types enabled globally
- Implicit usings enabled
- NUnit + NSubstitute for testing
- Spydersoft.Platform.Hosting for telemetry and health checks
