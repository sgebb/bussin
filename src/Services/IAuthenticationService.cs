using Azure.Core;

namespace Bussin.Services;

public interface IAuthenticationService
{
    Task<bool> IsAuthenticatedAsync();
    Task<TokenCredential?> GetTokenCredentialAsync();
    Task<string?> GetServiceBusTokenAsync();
    Task SignInAsync();
    Task SignOutAsync();
    Task<string?> GetUserNameAsync();
    string? GetUserName(); // Deprecated - use GetUserNameAsync
}
