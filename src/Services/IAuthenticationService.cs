using Azure.Core;
using Bussin.Models;

namespace Bussin.Services;

public interface IAuthenticationService
{
    bool IsDemoMode { get; }
    Task<bool> IsAuthenticatedAsync();

    /// <summary>Redirects to Entra; consents to both Azure Resource Manager and Service Bus in one prompt.</summary>
    Task SignInAsync();
    Task SignOutAsync();

    Task SetTenantAsync(string? tenantId);
    Task<string?> GetCurrentTenantIdAsync();

    /// <summary>Azure Resource Manager token for the selected tenant, or null.</summary>
    Task<TokenCredential?> GetTokenCredentialAsync();
    /// <summary>Azure Resource Manager token for the home tenant, or null.</summary>
    Task<TokenCredential?> GetHomeTokenCredentialAsync();
    /// <summary>Service Bus token for the selected tenant, or null.</summary>
    Task<string?> GetServiceBusTokenAsync();

    /// <summary>Silently checks both tokens for the selected tenant and says what is wrong, if anything.</summary>
    Task<AccessStatus> CheckAccessAsync();
    /// <summary>One popup for the selected tenant that signs in again and/or consents to both resources.
    /// Returns null on success, otherwise a message for the user.</summary>
    Task<string?> GrantAccessAsync();
}
