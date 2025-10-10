using Azure.Core;

namespace ServiceBusExplorer.Blazor.Services;

public interface IAuthenticationService
{
    Task<bool> IsAuthenticatedAsync();
    Task<TokenCredential?> GetTokenCredentialAsync();
    Task<string?> GetServiceBusTokenAsync();
    Task SignInAsync();
    Task SignOutAsync();
    string? GetUserName();
}
