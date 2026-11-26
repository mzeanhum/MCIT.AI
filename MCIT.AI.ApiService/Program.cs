using MCIT.AI.ApiService.Chat;
using MCIT.AI.Application.Chat;
using MCIT.AI.Infrastructure.SqlServer;
using MCIT.AI.Infrastructure.Ollama;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
builder.Services.AddOllamaChat(builder.Configuration);
if (builder.Configuration.GetConnectionString("ChatDatabase") is { Length: > 0 } chatConnection)
{
    builder.Services.AddSingleton<IChatStore>(new SqlChatStore(chatConnection));
}

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<ActiveChatTurns>();
string chatTimeZone = builder.Configuration["Chat:TimeZoneId"] ?? "Asia/Riyadh";
builder.Services.AddSingleton(new ChatClock(TimeProvider.System, TimeZoneInfo.FindSystemTimeZoneById(chatTimeZone)));

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => Results.Ok(new { Service = "MCIT.AI.ApiService" }));

app.MapChatEndpoints();
app.MapDefaultEndpoints();

app.Run();
