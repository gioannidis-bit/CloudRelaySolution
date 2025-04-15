using CloudRelayService.Hubs;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Increase max request body size (1GB)
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 1000L * 1024 * 1024;
});

builder.Services.AddRazorPages();
builder.Services.AddControllers();

// Add SignalR with MessagePack protocol registered.
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = 1000L * 1024 * 1024; // 1GB
});


var app = builder.Build();

app.UseStaticFiles();
app.MapRazorPages();
app.MapControllers();
app.MapHub<AgentHub>("/agentHub");

app.Run();
