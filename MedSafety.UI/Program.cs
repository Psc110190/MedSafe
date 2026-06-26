var builder = WebApplication.CreateBuilder(args);

// CORS support (not strictly needed for same-machine serving, but handy)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();   // Serves index.html for "/"
app.UseStaticFiles();    // Serves wwwroot/**

// Injects API base URL as a small JS config file so the frontend knows where
// the API lives without any build step.
app.MapGet("/config.js", (IConfiguration config) =>
{
    var apiBaseUrl = config["ApiBaseUrl"] ?? "http://localhost:5202/api";
    var js = $"window.API_BASE_URL = '{apiBaseUrl}';";
    return Results.Content(js, "application/javascript");
});

app.Run();
