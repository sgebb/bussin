using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Bussin;
using Bussin.Services;
using Bussin.Services.Demo;

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
    builder.Services.AddScoped<IAccessTokenProvider, DemoAccessTokenProvider>();
    
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
    // Real Auth
    builder.Services.AddMsalAuthentication(options =>
    {
        builder.Configuration.Bind("AzureAd", options.ProviderOptions.Authentication);
        
        // Request Service Bus data plane permission at login (required for send/receive/peek operations)
        // Management API scope (for listing namespaces) is requested later
        options.ProviderOptions.DefaultAccessTokenScopes.Add("https://servicebus.azure.net/user_impersonation");
        
        options.ProviderOptions.LoginMode = "redirect";
        options.ProviderOptions.Cache.CacheLocation = "localStorage";
    });

    builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
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

await builder.Build().RunAsync();
