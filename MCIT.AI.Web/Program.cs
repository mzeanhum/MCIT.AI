using MCIT.AI.Web.Components;
using MCIT.AI.Web.Chat;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();
builder.Services.AddChatBrowserIdentity(builder.Environment.IsDevelopment());

#pragma warning disable EXTEXP0001 // A streamed chat POST must not be retried automatically.
builder.Services.AddHttpClient<ChatApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ChatApi:BaseAddress"] ?? "https+http://apiservice");
}).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseChatBrowserIdentity();
app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
