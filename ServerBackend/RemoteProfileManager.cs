using Core.Models.Credentials;
using Microsoft.AspNetCore.Mvc;
using ProfileServer.DTO;
using ProfileServer.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<ServerDbContext>(opt =>
    opt.UseSqlite("Data Source=app.db"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
    db.Database.Migrate();
}

static async Task<UsernameAccount?> Authorize(ServerDbContext db, Guid userId, string key)
{
    var account = await db.Accounts.FirstOrDefaultAsync(u => u.Account.Id == userId);
    return account is not null && Crypto.Verify(key, account.Account.Password) ? account : null; 
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

    return Results.Ok(new { userId = user.Id });
});

app.MapGet("/profiles", async (Guid userId, [FromHeader(Name = "X-Key")] string key, ServerDbContext db) =>
{
    if (await Authorize(db, userId, key) is null) 
        return Results.Unauthorized();

    var list = await db.Profiles.Where(p => p.UserId == userId).ToListAsync();
    return Results.Ok(list.Select(p =>
        JsonSerializer.Deserialize<SavedProfile>(Crypto.Decrypt(p.ProfileJson, key))).ToList());
});

app.MapPost("/profiles", async (Guid userId, SavedProfile profile, [FromHeader(Name = "X-Key")] string key, ServerDbContext db) =>
{
    if (await Authorize(db, userId, key) is null) 
        return Results.Unauthorized();

    await db.Profiles.AddAsync(
    new UserProfile
    {
        UserId = userId,
        ProfileJson = Crypto.Encrypt(JsonSerializer.Serialize(profile), key)
    });
    await db.SaveChangesAsync();
    return Results.Ok(profile);
});

app.Run("https://localhost:5227");
