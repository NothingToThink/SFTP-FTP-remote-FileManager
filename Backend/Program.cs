using Core.Implementations.Storage;
using Core.Interfaces.Manager;
using Core.Interfaces.Storage;
using Microsoft.AspNetCore.Connections;

var builder = WebApplication.CreateBuilder(args);

//builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();

builder.Services.AddControllers();


builder.Services.AddSingleton<IProfileStorage, JsonProfileStorage>();
builder.Services.AddSingleton<IConnectionManager>();
builder.Services.AddSingleton<IConnectionFactory>();


var app = builder.Build(); 