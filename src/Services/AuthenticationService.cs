using Azure.Core;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace ServiceBusExplorer.Blazor.Services;

public sealed class AuthenticationService(
    IAccessTokenProvider tokenProvider, 
    AuthenticationStateProvider authStateProvider) : IAuthenticationService
{
    public async Task<bool> IsAuthenticatedAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        return authState.User.Identity?.IsAuthenticated ?? false;
    }

    public async Task<TokenCredential?> GetTokenCredentialAsync()
    {
        try
        {
            // Get Management API token for ARM operations
            var result = await tokenProvider.RequestAccessToken(new AccessTokenRequestOptions
            {
                Scopes = ["https://management.azure.com/user_impersonation"]
            });

            if (result.TryGetToken(out var token))
            {
                Console.WriteLine($"✓ Got Management API token (expires: {token.Expires})");
                return new AccessTokenCredential(token.Value, token.Expires);
            }

            Console.WriteLine($"✗ Failed to get Management API token. Status: {result.Status}");
            if (result.Status == AccessTokenResultStatus.RequiresRedirect)
            {
                Console.WriteLine("⚠ Requires redirect - user needs to sign in");
                // The redirect will be handled automatically by MSAL
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Exception getting Management API token: {ex.Message}");
            return null;
        }
    }

    public async Task<string?> GetServiceBusTokenAsync()
    {
        // Get Service Bus token for message operations
        // User should have consented to this scope during initial login
        var result = await tokenProvider.RequestAccessToken(new AccessTokenRequestOptions
        {
            Scopes = ["https://servicebus.azure.net/user_impersonation"]
        });

        if (result.TryGetToken(out var token))
        {
            return token.Value;
        }

        Console.WriteLine($"✗ Failed to get Service Bus token. Status: {result.Status}");
        
        if (result.Status == AccessTokenResultStatus.RequiresRedirect)
        {
            Console.WriteLine("⚠ Service Bus scope requires consent.");
            Console.WriteLine("   This usually means the user needs to sign out and sign in again to consent to all required scopes.");
            Console.WriteLine("   Or admin consent wasn't granted for the Service Bus API permission.");
        }

        return null;
    }

    public Task SignInAsync()
    {
        // Navigation to login is handled by MSAL automatically
        return Task.CompletedTask;
    }

    public Task SignOutAsync()
    {
        return Task.CompletedTask;
    }

    public async Task<string?> GetUserNameAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        return authState.User.Identity?.Name;
    }
    
    public string? GetUserName()
    {
        // Synchronous version - deprecated, use GetUserNameAsync
        return null;
    }
}

// Helper class to wrap access token as TokenCredential
internal sealed class AccessTokenCredential(string token, DateTimeOffset? expiresOn = null) : TokenCredential
{
    private readonly DateTimeOffset _expiresOn = expiresOn ?? DateTimeOffset.UtcNow.AddHours(1);

    public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return new Azure.Core.AccessToken(token, _expiresOn);
    }

    public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return new ValueTask<Azure.Core.AccessToken>(new Azure.Core.AccessToken(token, _expiresOn));
    }
}
