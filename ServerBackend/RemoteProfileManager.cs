using Core.Models.Credentials;
using ProfileServer.DTO;
using ProfileServer.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<ServerDbContext>(opt =>
    opt.UseSqlite("Data Source=app.db"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
    db.Database.Migrate();
}

var storageLock = new object();

app.MapPost("/auth/register", async (RegisterRequest request, ServerDbContext db) =>
{
    if(string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest("Username and password are required.");

    if (await db.Accounts.AnyAsync(u => u.Username == request.Username))
        return Results.Conflict("Username already exists.");

    Guid id = Guid.NewGuid();

    await db.Accounts.AddAsync(new UsernameAccount {
        Username = request.Username,
        Account = new UserAccount(id, request.Password)
    });
    await db.SaveChangesAsync();

    return Results.StatusCode(201);
});

app.MapPost("/auth/login", async (LoginRequest request, ServerDbContext db) =>
{
    if(string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest("Username and password are required.");

    var user = (await db.Accounts.FindAsync(request.Username))?.Account;
    if (user is null) return Results.Unauthorized();
    if(user.Password != request.Password)
        return Results.Unauthorized();

    return Results.Ok(new { userId = user.Id });
});

app.MapGet("/profiles", async (Guid userId, ServerDbContext db) =>
{
    if (!await db.Accounts.AnyAsync(u => u.Account.Id == userId)) 
        return Results.Unauthorized();

    return Results.Ok(db.Profiles.Where(p => p.UserId == userId).Select(p => p.Profile).ToList());
});

app.MapPost("/profiles", async (Guid userId, SavedProfile profile, ServerDbContext db) =>
{
    if (!await db.Accounts.AnyAsync(u => u.Account.Id == userId)) 
        return Results.Unauthorized();

    await db.Profiles.AddAsync(
    new UserProfile {
            UserId = userId,
            Profile = profile
    });
    return Results.Ok(profile);
});

app.Run("https://localhost:5227");
