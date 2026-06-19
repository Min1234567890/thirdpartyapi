using ThirdpartyAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// Register services for dependency injection
builder.Services.AddSingleton<JCMSLookup>();
builder.Services.AddSingleton<HealthService>();
builder.Services.AddSingleton<EnrolmentService>();
builder.Services.AddSingleton<VerifyService>();

// Add controllers
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

Console.WriteLine("ThirdpartyAPI (.NET Core) running on http://localhost:5000");
Console.WriteLine("  POST /api/enrol");
Console.WriteLine("  POST /api/verify");
Console.WriteLine("  GET  /api/health");

app.Run();
