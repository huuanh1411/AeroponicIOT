using AeroponicIOT.Data;
using AeroponicIOT.Models;
using AeroponicIOT.Services.Automation;
using AeroponicIOT.Services.Mqtt;
using AeroponicIOT.Services.Notifications;
using AeroponicIOT.Services.Sensors;
using AeroponicIOT.Services.Maintenance;
using AeroponicIOT.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"];
if (string.IsNullOrWhiteSpace(secretKey))
{
    throw new InvalidOperationException("JWT SecretKey is not configured. Set JwtSettings:SecretKey via environment or configuration.");
}

var key = Encoding.ASCII.GetBytes(secretKey);

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
        ValidIssuer = jwtSettings["Issuer"],
        ValidateAudience = true,
        ValidAudience = jwtSettings["Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Administrator"));
    options.AddPolicy("FarmerOrAdmin", policy => policy.RequireRole("Farmer", "Administrator"));
});

// Add DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add MQTT Service
builder.Services.AddSingleton<IMqttService, MqttService>();

// Add Notification Services
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<INotificationService, NotificationService>();

// Sensor ingestion (shared by HTTP and MQTT)
builder.Services.AddScoped<ISensorIngestionService, SensorIngestionService>();

// Automation background service
builder.Services.AddHostedService<AutomationBackgroundService>();
builder.Services.AddHostedService<LogRetentionBackgroundService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

// Serve static files
app.UseStaticFiles();

// Health endpoint (DB + MQTT)
app.MapGet("/health", async (ApplicationDbContext db, IMqttService mqtt, CancellationToken ct) =>
{
    try
    {
        var canConnect = await db.Database.CanConnectAsync(ct);
        if (!canConnect)
        {
            return Results.Json(new
            {
                status = "Unhealthy",
                db = "Unavailable",
                mqtt = mqtt.IsRunning ? "Running" : "Stopped",
                timestamp = DateTime.UtcNow
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            status = "Unhealthy",
            db = "Error",
            mqtt = mqtt.IsRunning ? "Running" : "Stopped",
            error = ex.Message,
            timestamp = DateTime.UtcNow
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (!mqtt.IsRunning)
    {
        return Results.Json(new
        {
            status = "Unhealthy",
            db = "Connected",
            mqtt = "Stopped",
            timestamp = DateTime.UtcNow
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new
    {
        status = "Healthy",
        db = "Connected",
        mqtt = "Running",
        timestamp = DateTime.UtcNow
    });
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
        dbContext.Database.Migrate();
        await SeedDefaultCropsAsync(dbContext, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while migrating or initializing the database.");
        // Let the app continue to run; in container environments the DB may come up after the app.
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

app.Run();

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
