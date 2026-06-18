var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin();
var filestoreDb = postgres.AddDatabase("filestore");

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

// MinIO (S3-compatible local substitute for Garage)
var minio = builder.AddContainer("minio", "minio/minio")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithEnvironment("MINIO_ROOT_USER", "minioadmin")
    .WithEnvironment("MINIO_ROOT_PASSWORD", "minioadmin")
    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "s3")
    .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console");

// Aspire dashboard OTLP endpoint
var dashboardOtlp = builder.Configuration["DOTNET_DASHBOARD_OTLP_ENDPOINT_URL"]
    ?? "http://localhost:18889";

// FileStoreApi
var api = builder.AddProject<Projects.Spydersoft_FileStoreApi>("filestore-api")
    .WithReference(filestoreDb)
    .WithReference(rabbitmq)
    .WaitFor(postgres)
    .WaitFor(rabbitmq)
    .WaitFor(minio);

// MinIO connection info — AccessKey/SecretKey/BucketName are the same everywhere
api.WithEnvironment("Storage__AccessKey", "minioadmin")
   .WithEnvironment("Storage__SecretKey", "minioadmin")
   .WithEnvironment("Storage__BucketName", "filestore");

// Telemetry env vars (same pattern as audit template)
foreach (var (typeKey, endpointKey) in new[]
{
    ("Telemetry__Trace__Type",   "Telemetry__Trace__Otlp__Endpoint"),
    ("Telemetry__Metrics__Type", "Telemetry__Metrics__Otlp__Endpoint"),
    ("Telemetry__Log__Type",     "Telemetry__Log__Otlp__Endpoint"),
})
{
    api.WithEnvironment(typeKey, builder.Configuration[typeKey] ?? "otlp");
    api.WithEnvironment(endpointKey, builder.Configuration[endpointKey] ?? dashboardOtlp);
}

// Environment-specific auth and storage configuration
if (builder.Environment.EnvironmentName == "Testing")
{
    var testKey = builder.Configuration["Auth:TestKey"]
        ?? "jRv3YFPH/19t9t5CgsEFgAkykfW5bQhHmceMprLgzlQ=";

    // In Testing, use a hard-coded HTTP URL instead of the Aspire endpoint reference.
    // The DCP endpoint reference resolves to HTTPS even for WithHttpEndpoint, which
    // causes the AWS SDK to generate https:// presigned URLs that MinIO rejects.
    api.WithEnvironment("DOTNET_ENVIRONMENT", "Testing")
       .WithEnvironment("Auth__TestKey", testKey)
       .WithEnvironment("Storage__ServiceUrl", "http://localhost:9000")
       .WithEndpoint("http", e => e.Port = 5300);
}
else
{
    api.WithEnvironment("Storage__ServiceUrl", minio.GetEndpoint("s3"))
       .WithEnvironment("Auth__Authority",
            builder.Configuration["Auth:Authority"] ?? "https://auth.mattgerega.net")
       .WithEnvironment("Auth__Audience",
            builder.Configuration["Auth:Audience"] ?? "filestore-api");
}

await builder.Build().RunAsync();
