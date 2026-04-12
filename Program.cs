using ExpenseManagerAPI.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

// Fix Npgsql timestamp behavior: cho phép DateTime Kind=Unspecified map sang timestamp (không ép UTC)
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var config = builder.Configuration;
var env = builder.Environment;

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<SoChungDbContext>(options =>
    options.UseNpgsql(
        config.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.EnableRetryOnFailure(3)
    ));

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
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ── JWT Authentication ────────────────────────────────────────────────────────
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

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var blacklist = context.HttpContext.RequestServices
                    .GetRequiredService<ExpenseManagerAPI.Services.ITokenBlacklistService>();

                var jti = context.Principal?
                    .FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?
                    .Value;

                if (jti != null && blacklist.IsRevoked(jti))
                    context.Fail("Token đã bị thu hồi.");

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ── CORS ──────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// ── Enable PostgreSQL extensions cần thiết ────────────────────────────────────
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SoChungDbContext>();
    await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS unaccent;");
    Console.WriteLine("[Startup] unaccent extension enabled.");
}
catch (Exception ex)
{
    Console.WriteLine($"[Startup] WARNING: Could not enable unaccent extension: {ex.Message}");
}

// ── Middleware Pipeline ───────────────────────────────────────────────────────
if (env.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SOCHUNG API v1"));
}

app.UseCors("AllowAll");

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/", () => "SOCHUNG API is running!");

app.Run();