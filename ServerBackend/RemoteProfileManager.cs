using Core.Models.Credentials;
using ProfileServer.DTO;
using ProfileServer.Models;


var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var users = new Dictionary<string, UserAccount>();
var profiles = new Dictionary<Guid, List<SavedProfile>>();
var storageLock = new object();

app.MapPost("/auth/register", (RegisterRequest request) =>
{
    if(string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest("Username and password are required.");

    lock (storageLock)
    {
        if(users.ContainsKey(request.Username))
            return Results.Conflict("Username already exists.");
        
        Guid id = Guid.NewGuid();
        users[request.Username] = new UserAccount(id, request.Password);
        profiles[id] = new List<SavedProfile>();

        return Results.StatusCode(201);
    }
});

app.MapPost("/auth/login", (LoginRequest request) =>
{
    if(string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest("Username and password are required.");

    lock (storageLock)
    {
        if(!users.ContainsKey(request.Username))
            return Results.Unauthorized();
        
        var user = users[request.Username];
        if(user.Password != request.Password)
            return Results.Unauthorized();

        return Results.Ok(new { userId = user.Id });
    }
});

app.MapGet("/profiles", (Guid userId) =>
{
    lock (storageLock)
    {
        if(!profiles.ContainsKey(userId))
            return Results.Unauthorized();

        return Results.Ok(new List<SavedProfile>(profiles[userId]));
    }   
});

app.MapPost("/profiles", (Guid userId, ProfileRequest request) =>
{
    lock (storageLock)
    {
        if(!profiles.ContainsKey(userId))
            return Results.Unauthorized();

        if(string.IsNullOrWhiteSpace(request.Name) || request.HostProfile == null)
            return Results.BadRequest("Name and HostProfile are required.");

        var profile = SavedProfile.Create(request.Name, request.HostProfile);
        profiles[userId].Add(profile);
        return Results.Ok(profile);
    }
});

app.Run("https://localhost:5227");