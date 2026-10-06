using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Middleware;
using Backend.Services;
using Core.Implementations.Factory;
using Core.PortForwarding;
using Core.PortForwarding.Discovery;
using Core.Ssh;
using Core.Ssh.HostKey;
using Microsoft.Extensions.Options;
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
            options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        });

    builder.Services.AddSingleton<ICredentialProtectionService, Base64CredentialProtectionService>();
    builder.Services.AddSingleton<IProfileStorage>(sp =>
    {
        var protection = sp.GetRequiredService<ICredentialProtectionService>();
        return new JsonProfileStorage(AppPaths
            .GetProfilesFilePath(), protection);
    });

    builder.Services.Configure<SshSessionOptions>(builder.Configuration.GetSection("Ssh"));
    builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<SshSessionOptions>>().Value);

    builder.Services.AddSingleton<IHostKeyStore>(_ => new KnownHostsStore(AppPaths.GetKnownHostsFilePath()));
    builder.Services.AddSingleton<IRemotePortScanner, SshRemotePortScanner>();
    builder.Services.AddSingleton<IPortForwardingManager, PortForwardingManager>();

    builder.Services.AddSingleton<IConnectionFactory, ConnectionFactory>();
    builder.Services.AddSingleton<IConnectionManager, ConnectionManager>();
    builder.Services.AddSingleton<IProfileManager, ProfileManager>();

    builder.Services.AddHostedService<IdleConnectionJanitor>();


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
