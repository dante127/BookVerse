using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using BookVerse.Api.Middleware;
using BookVerse.Application;
using BookVerse.Application.Common.Models;
using BookVerse.Infrastructure;
using BookVerse.Infrastructure.Persistence;
using BookVerse.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Add Application & Infrastructure
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddHttpContextAccessor();

// Authentication & JWT
var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret) || Encoding.UTF8.GetByteCount(jwtSecret) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Secret is missing or shorter than 32 bytes. " +
        "Configure it via environment (Jwt__Secret), a secret store, or appsettings.Development.json. " +
        "Refusing to start with an unset or weak signing key.");
}

if (builder.Environment.IsProduction()
    && string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("DefaultConnection")))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not configured in Production. Refusing to start.");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "BookVerse";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "BookVerseClients";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

// SEC-05: fixed-window rate limiting. Partitions are per-IP for unauthenticated traffic;
// per-account brute force is additionally capped by the DB-backed login lockout.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var response = context.HttpContext.Response;
        response.ContentType = "application/json";

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }

        var body = ApiResponse.Fail("Too many requests. Please try again later.");
        await response.WriteAsync(JsonSerializer.Serialize(body, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }), cancellationToken);
    };

    options.AddPolicy("login", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy("auth", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// CORS is opt-in per environment via config; without configured origins the API
// stays non-browsable cross-origin (server-to-server only) instead of wildcarding.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendClients", policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
    });
});

// Framework-level errors (404/405/401 challenges, status-only responses) get RFC 9457 bodies.
builder.Services.AddProblemDetails();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "BookVerse API",
        Version = "v1",
        Description = "Enterprise Book, Novel & Reading Platform Backend API"
    });

    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and your token.",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    };

    c.AddSecurityDefinition("Bearer", securityScheme);

    c.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer"),
            new List<string>()
        }
    });
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("database");

var app = builder.Build();

// Middlewares
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Baseline protective headers on every response.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "BookVerse API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseCors("FrontendClients");

// Give status-only responses (404/405, bare auth challenges) an RFC 9457 body.
app.UseStatusCodePages(async context =>
{
    var response = context.HttpContext.Response;
    if (response.HasStarted) return;

    response.ContentType = "application/problem+json";
    var problem = new
    {
        type = "https://httpstatuses.com/" + response.StatusCode,
        title = "HTTP error",
        status = response.StatusCode
    };
    await response.WriteAsync(JsonSerializer.Serialize(problem, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }));
});

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");

// Database migration and seeding.
// In Production any failure is fatal; in Development the app may boot against an offline DB (logged as warning).
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (db.Database.IsSqlServer() && !app.Environment.IsEnvironment("Testing"))
        {
            await db.Database.MigrateAsync();

            var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
            // Sample content (books, readers, demo accounts) is seeded in Development only.
            // Production seeds roles/permissions and, when Seed:AdminEmail/AdminPassword are
            // configured, a single bootstrap admin.
            await seeder.SeedAsync(withSampleData: app.Environment.IsDevelopment());
        }
    }
    catch (Exception ex) when (!app.Environment.IsDevelopment())
    {
        app.Logger.LogCritical(ex, "Database migration/seeding failed. Aborting startup.");
        throw;
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not run database migration/seed on startup (database may be offline).");
    }
}

app.Run();

public partial class Program { }
