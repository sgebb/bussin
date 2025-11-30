using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ServiceBusExplorer.Blazor;
using ServiceBusExplorer.Blazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

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
builder.Services.AddSingleton<ServiceBusEntityCache>();
builder.Services.AddSingleton<IAzureResourceService, AzureResourceService>();
builder.Services.AddScoped<IServiceBusJsInteropService, ServiceBusJsInteropService>();
builder.Services.AddScoped<IServiceBusOperationsService, ServiceBusOperationsService>();
builder.Services.AddScoped<IPreferencesService, PreferencesService>();
builder.Services.AddScoped<IMessageParsingService, MessageParsingService>();
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddScoped<NavigationStateService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddSingleton<BackgroundPurgeService>();

await builder.Build().RunAsync();
