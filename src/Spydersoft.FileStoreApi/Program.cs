using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Spydersoft.FileStoreApi;
using Spydersoft.FileStoreApi.Endpoints;
using Spydersoft.FileStoreApi.Infrastructure.Data;
using Spydersoft.FileStoreApi.Infrastructure.Storage;
using Spydersoft.FileStoreApi.Services;
using Spydersoft.Platform.Hosting.StartupExtensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddSpydersoftTelemetry(typeof(Program).Assembly)
       .AddSpydersoftSerilog();

var healthCheckOptions = builder.AddSpydersoftHealthChecks();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        if (builder.Environment.IsEnvironment("Testing"))
        {
            var testKey = builder.Configuration["Auth:TestKey"]!;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = false,
                IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(testKey)),
            };
        }
        else
        {
            options.Authority = builder.Configuration["Auth:Authority"];
            options.Audience = builder.Configuration["Auth:Audience"];
        }
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthorizationPolicies.Read, p => p.RequireClaim("scope", AuthorizationPolicies.Read))
    .AddPolicy(AuthorizationPolicies.Write, p => p.RequireClaim("scope", AuthorizationPolicies.Write));

builder.Services.AddDbContext<FileStoreDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("filestore"))
           .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.AddSingleton<IStorageClient, S3StorageClient>();

builder.Services.AddScoped<AuditEventService>();
builder.Services.AddScoped<RetentionPolicyService>();

builder.Services.AddControllers();
builder.Services.AddFileStoreOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FileStoreDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsEnvironment("Testing"))
{
    var opts = app.Services.GetRequiredService<IOptions<StorageOptions>>().Value;
    var s3Config = new AmazonS3Config
    {
        ServiceURL = opts.ServiceUrl,
        ForcePathStyle = true,
        AuthenticationRegion = opts.Region,
    };
    using var s3 = new AmazonS3Client(new BasicAWSCredentials(opts.AccessKey, opts.SecretKey), s3Config);
    try
    {
        await s3.PutBucketAsync(new PutBucketRequest { BucketName = opts.BucketName, UseClientRegion = true });
    }
    catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
    {
        // Already exists — no action needed.
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

if (app.Environment.IsEnvironment("Testing"))
{
    app.MapTestEndpoints();
}

app.UseSpydersoftHealthChecks(healthCheckOptions);

await app.RunAsync();
