using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Middleware;
using Core.Implementations.Factory;
using Core.Implementations.Manager;
using Core.Implementations.Storage;
using Core.Interfaces.Factory;
using Core.Interfaces.Manager;
using Core.Interfaces.Storage;
using Core.Security;
using Core.Utils;

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });

    builder.Services.AddSingleton<ICredentialProtectionService, Base64CredentialProtectionService>();
    builder.Services.AddSingleton<IProfileStorage>(sp =>
    {
        var protection = sp.GetRequiredService<ICredentialProtectionService>();
        return new JsonProfileStorage(AppPaths
            .GetProfilesFilePath(), protection);
    });
    
    builder.Services.AddSingleton<IConnectionFactory, ConnectionFactory>();
    builder.Services.AddSingleton<IConnectionManager, ConnectionManager>();
    builder.Services.AddSingleton<IProfileManager, ProfileManager>();


    using var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    
    app.UseCors(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
    app.UseMiddleware<ExceptionMiddleware>();
    app.MapControllers();
    app.Run();
}
catch (Exception e)
{
    Console.WriteLine($"Fatal error: {e.Message}");
    Console.WriteLine(e.StackTrace);
}