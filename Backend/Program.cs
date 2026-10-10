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
using Core.Implementations.ServerClient;
using Core.Interfaces.Factory;
using Core.Interfaces.Manager;
using Core.Interfaces.Storage;
using Core.Interfaces.ServerClient;
using Core.Security;
using Core.Utils;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

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
        })
        .ConfigureApiBehaviorOptions(options =>
        {
            // Same {"error": ...} shape as ExceptionMiddleware instead of ProblemDetails:
            // the client only reads the "error" field.
            options.InvalidModelStateResponseFactory = context =>
            {
                var queryParameters = context.ActionDescriptor.Parameters
                    .Where(p => p.BindingInfo?.BindingSource == BindingSource.Query)
                    .Select(p => p.Name)
                    .ToHashSet();
                var messages = context.ModelState
                    .Where(e => e.Value?.Errors.Count > 0)
                    .Select(e => queryParameters.Contains(e.Key) && string.IsNullOrEmpty(e.Value!.AttemptedValue)
                        ? $"Query parameter '{e.Key}' is required."
                        : string.Join(" ", e.Value!.Errors.Select(err =>
                            string.IsNullOrEmpty(err.ErrorMessage) ? $"Invalid value for '{e.Key}'." : err.ErrorMessage)));
                return new BadRequestObjectResult(new { error = string.Join("; ", messages) });
            };
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
    builder.Services.AddSingleton<IServerSessionService, ServerSessionService>();
    builder.Services.AddHttpClient<IProfileServerClient, ProfileServerClient>(client =>
    {
        client.BaseAddress = new Uri("https://sftp-ftp-server.duckdns.org");
    });
                                                                              
    builder.Services.AddHostedService<IdleConnectionJanitor>();


    builder.Services.AddSwaggerGen(options =>
    {
        var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
        options.IncludeXmlComments(xmlPath);
    });

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




public partial class Program { } 
