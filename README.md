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

## Codebase Conventions
- All C# classes sealed unless explicitly designed for inheritance
- Nullable reference types enabled globally
- Implicit usings enabled
- NUnit + NSubstitute for testing
- Spydersoft.Platform.Hosting for telemetry and health checks
