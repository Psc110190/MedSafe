using MedSafety.API.Services;
using System.Text.Json;
using System.Text.Json.Serialization;
var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Medication Safety Knowledge Service API",
        Version = "v1",
        Description = "A comprehensive API for medication safety screening. Identifies contraindications, " +
                      "black box warnings, drug interactions, allergy cross-reactivity, and use-with-caution " +
                      "alerts based on patient profiles including allergies, comorbidities, and current medications."
    });
});

builder.Services.AddSingleton<MedicationSafetyService>();
builder.Services.AddSingleton<ContextualAlertFilterService>();
builder.Services.AddSingleton<CustomSafetyRuleService>();
builder.Services.AddSingleton<PatientContextSafetyRuleService>();

// Register external drug data service with HttpClient
var timeoutSeconds = builder.Configuration.GetValue("ExternalDrugData:TimeoutSeconds", 15);
builder.Services.AddHttpClient<ExternalDrugDataService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("User-Agent", "MedSafety-KnowledgeService/1.0");
});

// Enable CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

// Swagger always on for this demo service
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MedSafety API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors();
app.MapControllers();

app.Run();
