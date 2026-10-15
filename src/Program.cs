using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Bussin;
using Bussin.Services;
using Bussin.Services.Demo;
using Bussin.Models;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

bool isDemoMode = false;
try 
{
    var tempProvider = builder.Services.BuildServiceProvider();
    var navMan = tempProvider.GetService<NavigationManager>();
    if (navMan != null)
    {
        var uri = navMan.Uri;
        isDemoMode = uri.Contains("/demo");
    }
}
catch
{
    // Fallback if something goes wrong during detection
    Console.WriteLine("Warning: Could not detect demo mode status, defaulting to production.");
}

if (isDemoMode)
{
    Console.WriteLine("Starting in Demo Mode");
    
    // Auth services for Demo
    builder.Services.AddAuthorizationCore();
    builder.Services.AddScoped<AuthenticationStateProvider, DemoAuthenticationStateProvider>();
    builder.Services.AddScoped<IAuthenticationService, DemoAuthenticationService>();
    
    // Demo implementations
    // Important: DemoServiceBusJsInteropService must be Singleton to share state with DemoAzureResourceService and persist data
    builder.Services.AddSingleton<DemoServiceBusJsInteropService>();
    builder.Services.AddSingleton<IServiceBusJsInteropService>(sp => sp.GetRequiredService<DemoServiceBusJsInteropService>());
    builder.Services.AddSingleton<IAzureResourceService, DemoAzureResourceService>();
    builder.Services.AddScoped<IMetricsService, DemoMetricsService>();
    builder.Services.AddSingleton<IPreferencesService, DemoPreferencesService>();
}
else
{
    // Real Auth: one service is both the auth state provider and the token source (see msal-helper.js)
    builder.Services.AddAuthorizationCore();
    builder.Services.AddScoped<AuthenticationService>();
    builder.Services.AddScoped<IAuthenticationService>(sp => sp.GetRequiredService<AuthenticationService>());
    builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AuthenticationService>());
    builder.Services.AddSingleton<IAzureResourceService, AzureResourceService>();
    builder.Services.AddScoped<IServiceBusJsInteropService, ServiceBusJsInteropService>();
    builder.Services.AddScoped<IMetricsService, MetricsService>();
    builder.Services.AddScoped<IPreferencesService, PreferencesService>();
}

// Common Services
builder.Services.AddSingleton<ServiceBusEntityCache>();
builder.Services.AddScoped<IServiceBusOperationsService, ServiceBusOperationsService>();
builder.Services.AddScoped<IMessageParsingService, MessageParsingService>();
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddScoped<IConfirmModalService, ConfirmModalService>();
builder.Services.AddScoped<NavigationStateService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddSingleton<BackgroundPurgeService>();
builder.Services.AddSingleton<BackgroundResubmitService>();
builder.Services.AddSingleton<BackgroundSearchService>();
builder.Services.AddScoped<EntitySelectionState>();
builder.Services.AddScoped<MessageListState>();
builder.Services.AddScoped<PeekService>();
builder.Services.AddScoped<SendService>();
builder.Services.AddScoped<MonitorService>();
builder.Services.AddScoped<SearchStateService>();
builder.Services.AddScoped<ExplorerDialogService>();

await builder.Build().RunAsync();
