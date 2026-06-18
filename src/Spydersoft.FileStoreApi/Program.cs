using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
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
    options.UseNpgsql(builder.Configuration.GetConnectionString("filestore")
        ?? throw new InvalidOperationException("ConnectionStrings:filestore is required.")));

builder.Services.AddOptions<StorageOptions>()
    .Bind(builder.Configuration.GetSection(StorageOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IStorageClient, S3StorageClient>();

builder.Services.AddScoped<RetentionPolicyService>();
builder.Services.AddSingleton<AuditEventService>();
builder.Services.AddHostedService<FileCleanupService>();

builder.Services.AddFileStoreOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapFileStoreEndpoints();
app.MapDocumentEndpoints();

app.UseSpydersoftHealthChecks(healthCheckOptions);

await app.RunAsync();
return 0;

public partial class Program { }
