using Core.Models.Credentials;
using Microsoft.AspNetCore.Mvc;
using ProfileServer.DTO;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.IO.Pipelines;
using Microsoft.Data.Sqlite;

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

app.MapPost("/auth/register", async (RegisterRequest request, ServerDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest("Username and password are required.");

    try
    {
        await db.Accounts.AddAsync(new UsernameAccount(request.Username, Crypto.Hash(request.Password)));
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
    {
        return Results.Conflict("Username already exists.");
    }

    return Results.StatusCode(201);
});

app.MapPost("/auth/login", async (LoginRequest request, ServerDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest("Username and password are required.");

    var account = await db.Accounts.FindAsync(request.Username);
    if (account is null || !Crypto.Verify(request.Password, account.HashedPassword))
        return Results.Unauthorized();
    var now = DateTime.UtcNow;
    var expiresAt = now.AddMinutes(jwtLifetimeMinutes);

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, account.Id.ToString()),
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


    return Results.Ok(new
    {
        accessToken = tokenString
    });
}).AllowAnonymous();

app.MapDelete("/account", async (ClaimsPrincipal user, ServerDbContext db) =>
{
    var sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

    if (!Guid.TryParse(sub, out var userId))
    {
        return Results.Unauthorized();
    }

    var account = await db.Accounts.FirstOrDefaultAsync(p => p.Id == userId);
    if (account is null)
        return Results.NotFound();

    try
    {
        db.Accounts.Remove(account);
        await db.SaveChangesAsync();
    } catch (DbUpdateConcurrencyException)
    {
        return Results.NoContent();
    }

    return Results.Ok();
}).RequireAuthorization();

app.MapPatch("/account/password", async (ChangePasswordRequest request, ClaimsPrincipal user, ServerDbContext db) =>
{
    var sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

    if(!Guid.TryParse(sub, out var userId))
    {
        return Results.Unauthorized();
    }

    var account = await db.Accounts.FirstOrDefaultAsync(p => p.Id == userId);
    if (account is null)
        return Results.NotFound();

    if (!Crypto.Verify(request.OldPassword, account.HashedPassword))
        return Results.BadRequest("Old password is incorrect");

    account.HashedPassword = Crypto.Hash(request.NewPassword);

    try {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.NotFound();
    }

    return Results.Ok();
}).RequireAuthorization();

app.MapGet("/profiles", async (ClaimsPrincipal user, ServerDbContext db) =>
{
    var sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

    if (!Guid.TryParse(sub, out var userId))
    {
        return Results.Unauthorized();
    }
    
    var list = await db.Profiles.Where(p => p.UserId == userId).ToListAsync();

    return Results.Ok(list.Select(p => new SavedProfile
    (
        p.Id,
        p.Name,
        JsonSerializer.Deserialize<HostProfile>(Crypto.Decrypt(p.JsonHostProfile))!
    )).ToList());
}).RequireAuthorization();

app.MapPost("/profiles", async (ClaimsPrincipal user, SavedProfile profile, ServerDbContext db) =>
{
    var sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

    if (!Guid.TryParse(sub, out var userId))
    {
        return Results.Unauthorized();
    }

    if (profile.Id == Guid.Empty)
    {
        return Results.BadRequest("Profile id is required.");
    }

    try
    {
        await db.Profiles.AddAsync(new UserProfile(userId, profile));
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
    {
        return Results.Ok(profile);
    }
    return Results.Ok(profile);
}).RequireAuthorization();

app.MapDelete("/profiles", async (ClaimsPrincipal user, Guid profileId, ServerDbContext db) =>
{
    var sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

    if(!Guid.TryParse(sub, out var userId))
    {
        return Results.Unauthorized();
    }

    if (profileId == Guid.Empty)
    {
        return Results.BadRequest("Profile id is required.");
    }
    var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == userId && p.Id == profileId);
    if (profile is null)
        return Results.NotFound();

    try
    {
        db.Profiles.Remove(profile);
        await db.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.NoContent();
    }
    return Results.Ok(profile);
}).RequireAuthorization();

app.Run("https://localhost:5227");
