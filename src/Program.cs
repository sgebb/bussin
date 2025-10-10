using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ServiceBusExplorer.Blazor;
using ServiceBusExplorer.Blazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Configure MSAL authentication
builder.Services.AddMsalAuthentication(options =>
{
    builder.Configuration.Bind("AzureAd", options.ProviderOptions.Authentication);
    // Request both Management API and Service Bus scopes
    options.ProviderOptions.DefaultAccessTokenScopes.Add("https://management.azure.com/user_impersonation");
    options.ProviderOptions.AdditionalScopesToConsent.Add("https://servicebus.azure.net/user_impersonation");
    options.ProviderOptions.LoginMode = "redirect";
});

// Register application services
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IAzureResourceService, AzureResourceService>();
builder.Services.AddScoped<IServiceBusJsInteropService, ServiceBusJsInteropService>();
builder.Services.AddScoped<IServiceBusOperationsService, ServiceBusOperationsService>();
builder.Services.AddScoped<IPreferencesService, PreferencesService>();
builder.Services.AddSingleton<INotificationService, NotificationService>();

await builder.Build().RunAsync();
