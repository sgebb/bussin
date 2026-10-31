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

    public Task<ArmToken?> GetTokenCredentialAsync()
    {
        return Task.FromResult<ArmToken?>(new ArmToken("demo-token", DateTimeOffset.UtcNow.AddHours(1)));
    }

    public Task<ArmToken?> GetHomeTokenCredentialAsync()
    {
        return Task.FromResult<ArmToken?>(new ArmToken("demo-token", DateTimeOffset.UtcNow.AddHours(1)));
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

    public Task<AccessStatus> CheckAccessAsync()
    {
        return Task.FromResult(new AccessStatus(AccessProblem.None, AccessProblem.None, null));
    }

    public Task<string?> GrantAccessAsync()
    {
        return Task.FromResult<string?>(null);
    }
}
