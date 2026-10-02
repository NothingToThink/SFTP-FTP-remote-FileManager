using Core.Models.Credentials;
using Microsoft.AspNetCore.Mvc;
using ProfileServer.DTO;
using ProfileServer.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.IO.Pipelines;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<ServerDbContext>(opt =>
    opt.UseSqlite("Data Source=app.db"));



/// Настройка JWT


var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("Не задан Jwt:Issuer.");

var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("Не задан Jwt:Audience.");

var jwtKeyBase64 = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Не задан Jwt:Key. Запусти init-server.sh.");

var jwtLifetimeMinutes =
    builder.Configuration.GetValue<int>("Jwt:LifetimeMinutes");

if(jwtLifetimeMinutes <= 0)
    throw new InvalidOperationException("Jwt:LifetimeMinutes должен быть положительным.");


var jwtKeyBytes = Convert.FromBase64String(jwtKeyBase64);

if (jwtKeyBytes.Length < 32)
    throw new InvalidOperationException("Ключ JWT должен содержать минимум 32 байта.");

var signingKey = new SymmetricSecurityKey(jwtKeyBytes);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(
        options =>
        {
            options.MapInboundClaims = false;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtIssuer,

                ValidateAudience = true,
                ValidAudience = jwtAudience,

                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,

                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,

                ValidAlgorithms = new[]
                {
                    SecurityAlgorithms.HmacSha256
                },

                ClockSkew = TimeSpan.Zero
            };
        }
    );

    builder.Services.AddAuthorization();

///    

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
    db.Database.Migrate();
}

static async Task<UsernameAccount?> IsAuthorized(ServerDbContext db, Guid userId)
{
    var account = await db.Accounts.FirstOrDefaultAsync(u => u.Account.Id == userId);
    return account;
}

app.MapPost("/auth/register", async (RegisterRequest request, ServerDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest("Username and password are required.");

    if (await db.Accounts.AnyAsync(u => u.Username == request.Username))
        return Results.Conflict("Username already exists.");

    await db.Accounts.AddAsync(new UsernameAccount {
        Username = request.Username,
        Account = new UserAccount(Guid.NewGuid(), Crypto.Hash(request.Password))
    });
    await db.SaveChangesAsync();

    return Results.StatusCode(201);
});

app.MapPost("/auth/login", async (LoginRequest request, ServerDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest("Username and password are required.");

    var user = (await db.Accounts.FindAsync(request.Username))?.Account;
    if (user is null || !Crypto.Verify(request.Password, user.Password))
        return Results.Unauthorized();
    var now = DateTime.UtcNow;
    var expiresAt = now.AddMinutes(jwtLifetimeMinutes);


    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
    };

    var signingCredentials = new SigningCredentials(
        signingKey,
        SecurityAlgorithms.HmacSha256
    );

    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        notBefore: now,
        expires: expiresAt,
        signingCredentials: signingCredentials
    );

    var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

    
    return Results.Ok( new
    {
        accessToken = tokenString
    });
}).AllowAnonymous();

app.MapGet("/profiles", async (ClaimsPrincipal user, ServerDbContext db) =>
{
    var sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

    if(!Guid.TryParse(sub, out var userId))
    {
        return Results.Unauthorized();
    }

    if (await IsAuthorized(db, userId) is null) 
        return Results.Unauthorized();

    var list = await db.Profiles.Where(p => p.UserId == userId).ToListAsync();
    return Results.Ok(list.Select(p =>
        JsonSerializer.Deserialize<SavedProfile>(Crypto.Decrypt(p.ProfileJson))).ToList());
}).RequireAuthorization();

app.MapPost("/profiles", async (ClaimsPrincipal user, SavedProfile profile, ServerDbContext db) =>
{

    var sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

    if(!Guid.TryParse(sub, out var userId))
    {
        return Results.Unauthorized();
    }

    if (await IsAuthorized(db, userId) is null)
        return Results.Unauthorized();
    
    if (profile.Id == Guid.Empty)
    {
        return Results.BadRequest("Profile id is required.");
    }

    var encrypted = Crypto.Encrypt(JsonSerializer.Serialize(profile));
    if (!await db.Profiles.AnyAsync(p => p.UserId == userId && p.ProfileJson == encrypted))
    {
        await db.Profiles.AddAsync(
        new UserProfile
        {
            UserId = userId,
            ProfileJson = encrypted
        });
        await db.SaveChangesAsync();
    }
    return Results.Ok(profile);
}).RequireAuthorization();

app.Run("https://localhost:5227");
