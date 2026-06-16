using Core.Implementations.Factory;
using Core.Implementations.Manager;
using Core.Implementations.Storage;
using Core.Interfaces.Factory;
using Core.Interfaces.Manager;
using Core.Interfaces.Storage;
using Core.Models.Credentials;
using Core.Security;
using Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using ConsoleApp;

var services = new ServiceCollection();

services.AddSingleton<ICredentialProtectionService, Base64CredentialProtectionService>();

services.AddSingleton<IProfileStorage>(provider =>
{
    var protection = provider.GetRequiredService<ICredentialProtectionService>();

    return new JsonProfileStorage(
        AppPaths.GetProfilesFilePath(),
        protection
    );
});

services.AddSingleton<IConnectionFactory, ConnectionFactory>();
services.AddSingleton<IProfileManager, ProfileManager>();
services.AddSingleton<IConnectionManager, ConnectionManager>();

var serviceProvider = services.BuildServiceProvider();

var profileManager = serviceProvider.GetRequiredService<IProfileManager>();
var connectionManager = serviceProvider.GetRequiredService<IConnectionManager>();

ConsoleState state = new();




while (true)
{
    if(!(state.CurrentConnection is null))
        Console.Write($"{state.ConnectionNames[state.CurrentConnectionId!.Value]}> ");
    
    string? input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
        continue;

    string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    string command = parts[0].ToLowerInvariant();

    switch (command)
    {
        case "help" :
        {
            ConsoleCommands.PrintHelp();
            break; 
        }
            

        case "profiles" :
        {
            ConsoleCommands.PrintProfiles(profileManager);
            break;
        }
            
        case "profile" :
        {
            if(parts.Length < 2)
            {
                Console.WriteLine("Usage: profile add/remove");
                break;
            }

            string subCommand = parts[1].ToLowerInvariant();

            switch (subCommand)
            {
                case "add" :
                {
                    ConsoleCommands.ProfileAdd(profileManager);
                    break;
                }
                case "remove" :
                {
                    ConsoleCommands.ProfileRemove(parts, profileManager);
                    break;
                }
                default:
                {
                    Console.WriteLine("Unknown profile command.");
                    break;
                }
            }
            break;
        }

        case "connections":
        {
            ConsoleCommands.Connections(state, connectionManager);
            break;
        }

        case "connect":
        {
            ConsoleCommands.Connect(parts, state, profileManager, connectionManager);
            break;
        }

        case "use":
        {
            ConsoleCommands.Use(parts, state, connectionManager);
            break;
        }

        case "disconnect":
        {
            ConsoleCommands.Disconnect(parts, state, connectionManager);
            break;
        }

        case "pwd":
        {
            ConsoleCommands.Pwd(state);
            break;
        }
            

        case "ls":
        {
            ConsoleCommands.Ls(parts, state);
            break;
        }
        case "cd":
        {
            ConsoleCommands.Cd(parts, state);
            break;
        }

        case "get":
        {
            ConsoleCommands.Get(parts, state);
            break;
        }

        case "put":
        {
            ConsoleCommands.Put(parts, state);
            break;
        }

        case "touch":
        {
            ConsoleCommands.Touch(parts, state);
            break;
        }

        case "rm":
        {
            ConsoleCommands.Rm(parts, state);
            break;
        }

        case "mkdir":
        {
            ConsoleCommands.Mkdir(parts, state);
            break;
        }

        case "rmdir":
        {
            ConsoleCommands.Rmdir(parts, state);
            break;
        }

        case "exit":
        {
            return;        
        }

        default:
        {
            Console.WriteLine("Unknown command.");
            break;
        }
    }
}