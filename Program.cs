using AeroponicIOT.Data;
using AeroponicIOT.Models;
using AeroponicIOT.Services.AI;
using AeroponicIOT.Services.Automation;
using AeroponicIOT.Services.BackgroundJobs;
using AeroponicIOT.Services.Caching;
using AeroponicIOT.Services.Mqtt;
using AeroponicIOT.Services.Notifications;
using AeroponicIOT.Services.Sensors;
using AeroponicIOT.Services.Maintenance;
using AeroponicIOT.Services.Security;
using AeroponicIOT.Middleware;
using AeroponicIOT.Options;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger / OpenAPI — single authoritative view of the entire API surface.
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "AeroponicIOT API",
        Version = "v1",
        Description = "REST API for the AeroponicIOT smart farming system — manages devices, sensors, actuators, crops, gardens, automation rules, and notifications. All endpoints return standardised `ApiResponse<T>` wrappers.",
        Contact = new()
        {
            Name = "AeroponicIOT"
        },
        License = new()
        {
            Name = "MIT"
        }
    });

    // Include XML doc comments from controller action summaries.
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }

    // JWT bearer token support in Swagger UI "Authorize" button.
    // Enables the "Authorize" button so users can paste their JWT token
    // and test authenticated endpoints directly from Swagger UI.
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme
    {
        Name = "Authorization",
        In = Microsoft.OpenApi.ParameterLocation.Header,
        Type = Microsoft.OpenApi.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter your JWT token"
    });
    // Add a global security requirement so all endpoints show the lock icon in Swagger UI.
    options.AddSecurityRequirement(static document =>
    {
        var scheme = document.Components?.SecuritySchemes?.FirstOrDefault(s => s.Key == "Bearer").Value;
        if (scheme == null) return new Microsoft.OpenApi.OpenApiSecurityRequirement();

        // OpenApi v2.x: the requirement dictionary uses OpenApiSecuritySchemeReference keys.
        var reference = new Microsoft.OpenApi.OpenApiSecuritySchemeReference("Bearer");
        return new Microsoft.OpenApi.OpenApiSecurityRequirement { { reference, new List<string>() } };
    });

    // Group endpoints by controller name for a clean Swagger UI layout.
    options.TagActionsBy(api =>
    {
        var controllerName = api.ActionDescriptor.RouteValues["controller"];
        return new[] { controllerName ?? "General" };
    });
    options.OrderActionsBy(api => api.RelativePath ?? api.HttpMethod ?? "");
});

builder.Services.AddOpenApi();

builder.Services.AddOptions<JwtSettingsOptions>()
    .Bind(builder.Configuration.GetSection("JwtSettings"))
    .ValidateDataAnnotations()
    .Validate(o => !string.IsNullOrWhiteSpace(o.SecretKey), "JwtSettings:SecretKey is required")
    .ValidateOnStart();

builder.Services.AddOptions<MqttSettingsOptions>()
    .Bind(builder.Configuration.GetSection("MqttSettings"))
    .ValidateDataAnnotations()
    .Validate(o => !o.EnableTls || !string.IsNullOrWhiteSpace(o.ServerCertificatePath), "MqttSettings:ServerCertificatePath is required when TLS is enabled")
    .Validate(o => !o.RequireClientCertificate || o.EnableTls, "MqttSettings:EnableTls must be true when RequireClientCertificate is enabled")
    .Validate(
        o => !o.EnableZigbee2MqttBridge || !o.EnforceZigbeeTopicAcl ||
             !string.IsNullOrWhiteSpace(o.ZigbeeBridgeUsername) ||
             !string.IsNullOrWhiteSpace(o.ZigbeeBridgeClientId),
        "MqttSettings:ZigbeeBridgeUsername or MqttSettings:ZigbeeBridgeClientId must be configured when Zigbee bridge ACL is enabled")
    .Validate(
        o => !o.RequireClientCertificate ||
             (o.AllowedClientCertificateIssuers.Length > 0 || o.AllowedClientCertificateThumbprints.Length > 0),
        "MqttSettings:AllowedClientCertificateIssuers or MqttSettings:AllowedClientCertificateThumbprints must be configured when client certificates are required")
    .ValidateOnStart();

builder.Services.AddOptions<ProvisioningOptions>()
    .Bind(builder.Configuration.GetSection("Provisioning"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<EmailSettingsOptions>()
    .Bind(builder.Configuration.GetSection("EmailSettings"))
    .ValidateDataAnnotations()
    .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.SmtpHost), "EmailSettings:SmtpHost is required when email is enabled")
    .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.FromEmail), "EmailSettings:FromEmail is required when email is enabled")
    .ValidateOnStart();

builder.Services.AddOptions<AppUrlsOptions>()
    .Bind(builder.Configuration.GetSection("AppUrls"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<OnboardingProtectionOptions>()
    .Bind(builder.Configuration.GetSection("OnboardingProtection"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<CorsOptions>()
    .Bind(builder.Configuration.GetSection("Cors"));

builder.Services.AddOptions<PerformanceBudgetOptions>()
    .Bind(builder.Configuration.GetSection("PerformanceBudgets"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("ConfiguredOrigins", policy =>
    {
        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>()
            ?? Array.Empty<string>();

        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyMethod()
                .AllowAnyHeader();
            return;
        }

        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
            return;
        }

        policy.SetIsOriginAllowed(_ => false)
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// Add JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettingsOptions>()!;
var key = Encoding.ASCII.GetBytes(jwtSettings.SecretKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtSettings.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Administrator"));
    options.AddPolicy("FarmerOrAdmin", policy => policy.RequireRole("Farmer", "Administrator"));
});

builder.Services.AddRateLimiter(options =>
{
    var authPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:Auth:PermitLimit") ?? 10;
    var authWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:Auth:WindowSeconds") ?? 60;
    var devicePermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:DeviceOnboarding:PermitLimit") ?? 20;
    var deviceWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:DeviceOnboarding:WindowSeconds") ?? 60;

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth", limiterOptions =>
    {
        limiterOptions.PermitLimit = authPermitLimit;
        limiterOptions.Window = TimeSpan.FromSeconds(authWindowSeconds);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });

    options.AddPolicy("device-onboarding", httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = devicePermitLimit,
            Window = TimeSpan.FromSeconds(deviceWindowSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var redisConfiguration = builder.Configuration["Redis:Configuration"];
if (!string.IsNullOrWhiteSpace(redisConfiguration))
{
    // Register direct Redis connection multiplexer for custom cache operations
    var redis = ConnectionMultiplexer.Connect(redisConfiguration);
    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

    // Register distributed cache backed by Redis
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConfiguration;
    });

    // Register custom cache service for domain-specific operations
    builder.Services.AddSingleton<ICacheService, RedisCacheService>();
}
else
{
    // Fallback to in-memory cache for development
    builder.Services.AddDistributedMemoryCache();
    // Provide a no-op cache service when Redis is not available
    builder.Services.AddSingleton<ICacheService, NoCacheService>();
}

builder.Services.AddScoped<IOnboardingAttemptTracker, DistributedOnboardingAttemptTracker>();
builder.Services.AddHttpContextAccessor();

// HTTP client for AI suggestion API calls
builder.Services.AddHttpClient("ai-proxy")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        // Respect proxy settings if configured via environment variables.
        UseProxy = true
    });
builder.Services.AddScoped<ICurrentUserService, HttpCurrentUserService>();
builder.Services.AddScoped<IResourceOwnershipService, ResourceOwnershipService>();

builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Application is running"), tags: new[] { "live" })
    .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready" })
    .AddCheck<MqttHealthCheck>("mqtt", tags: new[] { "ready" });

var serviceName = builder.Configuration["OpenTelemetry:ServiceName"] ?? "AeroponicIOT";
var otlpEndpoint = builder.Configuration["OpenTelemetry:Otlp:Endpoint"];
var traceSampleRatio = builder.Configuration.GetValue<double?>("OpenTelemetry:Tracing:SampleRatio") ?? 1.0d;
traceSampleRatio = Math.Clamp(traceSampleRatio, 0.0d, 1.0d);
var telemetryExcludedPaths = builder.Configuration
    .GetSection("OpenTelemetry:ExcludedPaths")
    .Get<string[]>()
    ?? new[] { "/health", "/health/live", "/health/ready" };

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing =>
    {
        tracing
            .SetSampler(new TraceIdRatioBasedSampler(traceSampleRatio))
            .AddAspNetCoreInstrumentation(options =>
            {
                options.Filter = httpContext =>
                    !telemetryExcludedPaths.Any(path =>
                        httpContext.Request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase));
            })
            .AddHttpClientInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
            });
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(
                "Microsoft.AspNetCore.Hosting",
                "Microsoft.AspNetCore.Server.Kestrel",
                "System.Net.Http",
                PerformanceBudgetMiddleware.MeterName)
            .AddRuntimeInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
            });
        }
    });

// Add DbContext with connection pooling for horizontal scalability
// Connection pooling is enabled by default in EF Core 6+ (pool size: 256, idle timeout: 600s)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
        sqlOptions.CommandTimeout(30);
    });
    // No tracking by default for better query performance in read-heavy dashboard
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});

// Add Hangfire for background job processing (persistent, retryable)
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHangfireServer();

// Register background job classes
builder.Services.AddScoped<AlertNotificationJob>();
builder.Services.AddScoped<AIAnalysisJob>();

// Add MQTT Service
builder.Services.AddSingleton<IMqttService, MqttService>();

// Add Notification Services
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<INotificationService, NotificationService>();

// AI Suggestion Service
builder.Services.AddOptions<AIOptions>()
    .Bind(builder.Configuration.GetSection("AISuggestions"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<IAISuggestionService, AISuggestionService>();
builder.Services.AddHostedService<AIAnalysisBackgroundService>();

// Sensor ingestion (shared by HTTP and MQTT)
builder.Services.AddScoped<ISensorIngestionService, SensorIngestionService>();

// Automation background service
builder.Services.AddHostedService<AutomationBackgroundService>();
builder.Services.AddHostedService<LogRetentionBackgroundService>();

var app = builder.Build();

var applyMigrationsOnStartup = builder.Configuration.GetValue<bool?>("Startup:ApplyMigrationsOnStartup") ?? true;
var seedDefaultCropsOnStartup = builder.Configuration.GetValue<bool?>("Startup:SeedDefaultCropsOnStartup") ?? true;
var failFastOnInitializationError = builder.Configuration.GetValue<bool?>("Startup:FailFastOnInitializationError")
    ?? app.Environment.IsProduction();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "AeroponicIOT API v1");
        options.RoutePrefix = "swagger";
        options.DefaultModelExpandDepth(2);
        options.DefaultModelsExpandDepth(-1);
        options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
        options.EnableTryItOutByDefault();
    });
}

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<PerformanceBudgetMiddleware>();
app.UseMiddleware<GlobalExceptionHandlingMiddleware>();
if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}
app.UseCors("ConfiguredOrigins");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Hangfire Dashboard (admin-only in production)
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() }
});

// Serve static files
app.UseStaticFiles();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = WriteHealthResponseAsync
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponseAsync
});
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponseAsync
});

// Default route to dashboard
app.MapGet("/", async context =>
{
    context.Response.ContentType = "text/html";
    await context.Response.SendFileAsync("wwwroot/index.html");
});

app.MapControllers();

// Ensure database connection (don't create if it doesn't exist)
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    // Apply migrations on startup so the container can initialize schema automatically.
    try
    {
        if (applyMigrationsOnStartup && dbContext.Database.IsRelational())
        {
            dbContext.Database.Migrate();
        }

        if (seedDefaultCropsOnStartup)
        {
            await SeedDefaultCropsAsync(dbContext, logger);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while migrating or initializing the database.");
        if (failFastOnInitializationError)
        {
            throw;
        }

        // Let the app continue to run when fail-fast is disabled;
        // in container environments the DB may come up after the app.
    }
}

// Start MQTT Broker
try
{
    var mqttService = app.Services.GetRequiredService<IMqttService>();
    await mqttService.StartAsync();
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "Failed to start MQTT broker");
}

// Configure graceful shutdown: allow in-flight requests to complete before stopping
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(async () =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Application is shutting down...");

    try
    {
        // Stop accepting new requests (wait up to 5 seconds for graceful completion)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        
        // Stop MQTT client gracefully
        var mqttService = app.Services.GetRequiredService<IMqttService>();
        await mqttService.StopAsync();
        
        // Stop Hangfire jobs (already configured to stop on shutdown)
        logger.LogInformation("MQTT service stopped, Hangfire jobs draining...");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error during graceful shutdown");
    }
});

app.Run();

static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";

    var payload = new
    {
        status = report.Status.ToString(),
        totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
        timestamp = DateTime.UtcNow,
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            description = entry.Value.Description,
            durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 2),
            error = entry.Value.Exception?.Message,
            data = entry.Value.Data
        })
    };

    return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
}

static async Task SeedDefaultCropsAsync(ApplicationDbContext dbContext, ILogger logger)
{
    if (await dbContext.Crops.AnyAsync())
    {
        return;
    }

    var crops = new List<Crop>
    {
        new()
        {
            Name = "Lettuce",
            Description = "Fast leafy green cycle for aeroponic production.",
            TotalDaysEst = 45,
            CreatedAt = DateTime.UtcNow,
            CropStages = new List<CropStage>
            {
                new() { StageName = "Germination", DayStart = 1, DayEnd = 7, PhMin = 5.5m, PhMax = 6.2m, PpmMin = 350, PpmMax = 600, WaterTempMin = 18, WaterTempMax = 22, HumidityMin = 70, HumidityMax = 85, PumpOnMinutes = 3, PumpOffMinutes = 12 },
                new() { StageName = "Vegetative", DayStart = 8, DayEnd = 30, PhMin = 5.8m, PhMax = 6.5m, PpmMin = 650, PpmMax = 950, WaterTempMin = 19, WaterTempMax = 23, HumidityMin = 60, HumidityMax = 75, PumpOnMinutes = 5, PumpOffMinutes = 10 },
                new() { StageName = "Harvest", DayStart = 31, DayEnd = 45, PhMin = 5.8m, PhMax = 6.4m, PpmMin = 700, PpmMax = 900, WaterTempMin = 18, WaterTempMax = 22, HumidityMin = 55, HumidityMax = 70, PumpOnMinutes = 5, PumpOffMinutes = 8 }
            }
        },
        new()
        {
            Name = "Basil",
            Description = "Herb profile with warmer water and steady nutrient demand.",
            TotalDaysEst = 60,
            CreatedAt = DateTime.UtcNow,
            CropStages = new List<CropStage>
            {
                new() { StageName = "Propagation", DayStart = 1, DayEnd = 10, PhMin = 5.6m, PhMax = 6.2m, PpmMin = 300, PpmMax = 500, WaterTempMin = 20, WaterTempMax = 24, HumidityMin = 70, HumidityMax = 85, PumpOnMinutes = 3, PumpOffMinutes = 12 },
                new() { StageName = "Leaf Growth", DayStart = 11, DayEnd = 40, PhMin = 5.8m, PhMax = 6.4m, PpmMin = 700, PpmMax = 1100, WaterTempMin = 20, WaterTempMax = 25, HumidityMin = 60, HumidityMax = 75, PumpOnMinutes = 4, PumpOffMinutes = 9 },
                new() { StageName = "Mature", DayStart = 41, DayEnd = 60, PhMin = 5.8m, PhMax = 6.3m, PpmMin = 900, PpmMax = 1200, WaterTempMin = 20, WaterTempMax = 24, HumidityMin = 55, HumidityMax = 70, PumpOnMinutes = 5, PumpOffMinutes = 8 }
            }
        },
        new()
        {
            Name = "Strawberry",
            Description = "Longer fruiting cycle with moderate nutrient and humidity targets.",
            TotalDaysEst = 90,
            CreatedAt = DateTime.UtcNow,
            CropStages = new List<CropStage>
            {
                new() { StageName = "Establishment", DayStart = 1, DayEnd = 21, PhMin = 5.6m, PhMax = 6.0m, PpmMin = 500, PpmMax = 800, WaterTempMin = 18, WaterTempMax = 21, HumidityMin = 65, HumidityMax = 80, PumpOnMinutes = 4, PumpOffMinutes = 12 },
                new() { StageName = "Flowering", DayStart = 22, DayEnd = 55, PhMin = 5.8m, PhMax = 6.2m, PpmMin = 900, PpmMax = 1200, WaterTempMin = 18, WaterTempMax = 22, HumidityMin = 60, HumidityMax = 75, PumpOnMinutes = 5, PumpOffMinutes = 10 },
                new() { StageName = "Fruiting", DayStart = 56, DayEnd = 90, PhMin = 5.8m, PhMax = 6.3m, PpmMin = 1100, PpmMax = 1400, WaterTempMin = 17, WaterTempMax = 21, HumidityMin = 55, HumidityMax = 70, PumpOnMinutes = 5, PumpOffMinutes = 8 }
            }
        }
    };

    dbContext.Crops.AddRange(crops);
    await dbContext.SaveChangesAsync();
    logger.LogInformation("Seeded {CropCount} default crops", crops.Count);
}

public partial class Program
{
}
