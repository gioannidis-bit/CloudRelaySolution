using CloudRelayService.Hubs;

var builder = WebApplication.CreateBuilder(args);

// ��������� �� max request body size (�.�., 100 MB)
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 1000L * 1024 * 1024; // 1GB
});

builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    // Set the maximum receive message size to 10 MB.
    options.MaximumReceiveMessageSize = 1000L * 1024 * 1024; // 1GB (��� SignalR, �� ����������)
});

var app = builder.Build();

app.UseStaticFiles();

// If you have old static files that conflict, disable default files:
// app.UseDefaultFiles();

app.MapRazorPages();
app.MapControllers();
app.MapHub<AgentHub>("/agentHub");

app.Run();

