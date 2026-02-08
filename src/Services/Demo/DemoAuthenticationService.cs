using Azure.Core;
using Bussin.Models;
using Bussin.Services;

namespace Bussin.Services.Demo;

public class DemoAuthenticationService : IAuthenticationService
{
    public bool IsDemoMode => true;

    public Task<bool> IsAuthenticatedAsync()
    {
        return Task.FromResult(true);
    }

    public Task<TokenCredential?> GetTokenCredentialAsync()
    {
        return Task.FromResult<TokenCredential?>(new DemoTokenCredential());
    }

    public Task<TokenCredential?> GetHomeTokenCredentialAsync()
    {
        return Task.FromResult<TokenCredential?>(new DemoTokenCredential());
    }

    public Task<string?> GetServiceBusTokenAsync()
    {
        return Task.FromResult<string?>("demo-token-12345");
    }

    public Task SignInAsync()
    {
        return Task.CompletedTask;
    }

    public Task SignOutAsync()
    {
        return Task.CompletedTask;
    }

    public Task<string?> GetUserNameAsync()
    {
        return Task.FromResult<string?>("Demo User");
    }

    public string? GetUserName()
    {
        return "Demo User";
    }

    private string? _currentTenantId;

    public Task SetTenantAsync(string? tenantId)
    {
        _currentTenantId = tenantId;
        return Task.CompletedTask;
    }

    public Task<string?> GetCurrentTenantIdAsync()
    {
        return Task.FromResult(_currentTenantId);
    }

    public Task<bool> AcquireTokenPopupAsync(string scope, string? tenantId, string? loginHint = null)
    {
        return Task.FromResult(true);
    }

    public Task ClearMsalCacheAsync()
    {
        return Task.CompletedTask;
    }
}
