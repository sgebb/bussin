using Azure.Core;

namespace Bussin.Services;

public interface IAuthenticationService
{
    Task<bool> IsAuthenticatedAsync();
    Task<TokenCredential?> GetTokenCredentialAsync();
    Task<TokenCredential?> GetHomeTokenCredentialAsync();
    Task<string?> GetServiceBusTokenAsync();
    Task SignInAsync();
    Task SignOutAsync();
    Task<string?> GetUserNameAsync();
    
    Task SetTenantAsync(string? tenantId);
    Task<string?> GetCurrentTenantIdAsync();
    Task<bool> AcquireTokenPopupAsync(string scope, string? tenantId, string? loginHint = null);
    Task ClearMsalCacheAsync();
    bool IsDemoMode { get; }
    string? GetUserName(); // Deprecated - use GetUserNameAsync
}
