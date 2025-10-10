using Azure.Core;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace ServiceBusExplorer.Blazor.Services;

public sealed class AuthenticationService(IAccessTokenProvider tokenProvider) : IAuthenticationService
{
    public async Task<bool> IsAuthenticatedAsync()
    {
        var result = await tokenProvider.RequestAccessToken(new AccessTokenRequestOptions
        {
            Scopes = ["https://management.azure.com/user_impersonation"]
        });
        
        return result.TryGetToken(out _);
    }

    public async Task<TokenCredential?> GetTokenCredentialAsync()
    {
        // Get Management API token for ARM operations
        var result = await tokenProvider.RequestAccessToken(new AccessTokenRequestOptions
        {
            Scopes = ["https://management.azure.com/user_impersonation"]
        });

        if (result.TryGetToken(out var token))
        {
            return new AccessTokenCredential(token.Value, token.Expires);
        }

        return null;
    }

    public async Task<string?> GetServiceBusTokenAsync()
    {
        // Get Service Bus token for message operations
        var result = await tokenProvider.RequestAccessToken(new AccessTokenRequestOptions
        {
            Scopes = ["https://servicebus.azure.net/user_impersonation"]
        });

        if (result.TryGetToken(out var token))
        {
            Console.WriteLine($"✓ Got Service Bus token (expires: {token.Expires})");
            return token.Value;
        }

        Console.WriteLine($"✗ Failed to get Service Bus token. Status: {result.Status}");
        if (result.Status == AccessTokenResultStatus.RequiresRedirect)
        {
            Console.WriteLine("⚠ Requires redirect - user needs to consent to Service Bus scope");
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

    public string? GetUserName()
    {
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
