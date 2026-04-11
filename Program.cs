using ExpenseManagerAPI.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var config = builder.Configuration;
var env = builder.Environment;

// ── Database ──────────────────────────────────────────────────────────────────
// Connection string is read from config/environment variable.
// Override via: ConnectionStrings__DefaultConnection="<prod-connection-string>"
builder.Services.AddDbContext<SoChungDbContext>(options =>
    options.UseNpgsql(config.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.EnableRetryOnFailure(3)));

// ── Application Services ──────────────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ExpenseManagerAPI.Services.ICategoryService, ExpenseManagerAPI.Services.CategoryService>();
builder.Services.AddScoped<ExpenseManagerAPI.Services.IDashboardService, ExpenseManagerAPI.Services.DashboardService>();
builder.Services.AddSingleton<ExpenseManagerAPI.Services.INotificationService, ExpenseManagerAPI.Services.NotificationService>();
builder.Services.AddHostedService<ExpenseManagerAPI.Jobs.DebtReminderJob>();
builder.Services.AddScoped<ExpenseManagerAPI.Repositories.IUserRepository, ExpenseManagerAPI.Repositories.UserRepository>();
builder.Services.AddScoped<ExpenseManagerAPI.Services.IJwtProvider, ExpenseManagerAPI.Services.JwtProvider>();
builder.Services.AddScoped<ExpenseManagerAPI.Services.IAuthService, ExpenseManagerAPI.Services.AuthService>();
builder.Services.AddScoped<ExpenseManagerAPI.Services.IEmailService, ExpenseManagerAPI.Services.EmailService>();
builder.Services.AddSingleton<ExpenseManagerAPI.Services.ITokenBlacklistService, ExpenseManagerAPI.Services.TokenBlacklistService>();

builder.Services.AddControllers();

// ── Swagger ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SOCHUNG API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ── JWT Authentication ────────────────────────────────────────────────────────
// Key, Issuer, and Audience are read from config.
// Override via environment variables: Jwt__Key, Jwt__Issuer, Jwt__Audience
var jwtKey = config["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured. Set it via appsettings or the Jwt__Key environment variable.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = config["Jwt:Issuer"],
            ValidAudience = config["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var blacklist = context.HttpContext.RequestServices
                    .GetRequiredService<ExpenseManagerAPI.Services.ITokenBlacklistService>();

                var jti = context.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
                if (jti != null && blacklist.IsRevoked(jti))
                    context.Fail("Token đã bị thu hồi.");

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ── CORS ──────────────────────────────────────────────────────────────────────
// Origins are read from Cors:AllowedOrigins (JSON array in appsettings).
//
// Development (appsettings.Development.json):
//   Lists Flutter Web dev server ports, e.g. http://localhost:20244.
//   Flutter mobile (emulator/simulator) is NOT a browser — no CORS entry needed.
//
// Production (appsettings.Production.json or env vars):
//   Set to your deployed Flutter Web domain, e.g. https://app.yourdomain.com.
//   Override individual entries via: Cors__AllowedOrigins__0="https://app.yourdomain.com"
builder.Services.AddCors(options =>
{
    options.AddPolicy("ConfiguredOrigins", policy =>
    {
        if (env.IsDevelopment())
        {
            policy
                .SetIsOriginAllowed(origin =>
                {
                    if (string.IsNullOrWhiteSpace(origin)) return false;
                    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;

                    return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                        || uri.Host.Equals("127.0.0.1");
                })
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        else
        {
            var allowedOrigins = config
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>() ?? Array.Empty<string>();

            policy
                .WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

// ── Kestrel / Host Binding ────────────────────────────────────────────────────
// By default ASP.NET Core binds to localhost only.
// To listen on all interfaces (required for Docker, VMs, or direct mobile access):
//   Set environment variable: ASPNETCORE_URLS="http://0.0.0.0:5009"
//   Or in launchSettings.json change applicationUrl to "http://0.0.0.0:5009"
// The launchSettings.json localhost binding is intentional for local dev safety.

var app = builder.Build();

// ── Middleware Pipeline ───────────────────────────────────────────────────────

// Swagger is available in Development only.
// For a staging/production API explorer, add a separate secured Swagger endpoint.
if (env.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SOCHUNG API v1"));
}

app.UseCors("ConfiguredOrigins");

// HTTPS redirection is useful behind a TLS-terminating reverse proxy in production.
// In production with a proxy, you may want to disable this and rely on the proxy for TLS.
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/", () => "SOCHUNG API is running!");

app.Run();
