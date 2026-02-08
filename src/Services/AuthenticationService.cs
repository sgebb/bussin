using Azure.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using System.Text.Json;

namespace Bussin.Services;

public sealed class AuthenticationService(
    IAccessTokenProvider tokenProvider, 
    AuthenticationStateProvider authStateProvider,
    NavigationManager navigationManager,
    IPreferencesService preferencesService,
    IJSRuntime jsRuntime,
    IConfiguration configuration) : IAuthenticationService
{
    public bool IsDemoMode => false;

    public async Task<bool> IsAuthenticatedAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        return authState.User.Identity?.IsAuthenticated ?? false;
    }

    public async Task SetTenantAsync(string? tenantId)
    {
        var prefs = await preferencesService.LoadPreferencesAsync();
        prefs.SelectedTenantId = tenantId;
        await preferencesService.SavePreferencesAsync(prefs);
    }

    public async Task<string?> GetCurrentTenantIdAsync()
    {
        var prefs = await preferencesService.LoadPreferencesAsync();
        return prefs.SelectedTenantId;
    }

    private bool _msalConfigured;
    private async Task EnsureMsalConfigAsync()
    {
        if (_msalConfigured) return;
        try 
        {
            var clientId = configuration["AzureAd:ClientId"];
            var authority = configuration["AzureAd:Authority"];
            await jsRuntime.InvokeVoidAsync("msalHelper.setConfig", new { clientId, authority });
            _msalConfigured = true;
        }
        catch { }
    }

    private async Task<string?> TryGetTokenFromJsAsync(string scope, string tenantId)
    {
        try
        {
            await EnsureMsalConfigAsync();
            var result = await jsRuntime.InvokeAsync<JsonElement>("msalHelper.acquireTokenSilent", scope, tenantId);
            if (result.TryGetProperty("success", out var success) && success.GetBoolean())
            {
                if (result.TryGetProperty("token", out var tokenProp))
                {
                    return tokenProp.GetString();
                }
            }
            else if (result.TryGetProperty("error", out var errorProp))
            {
                var error = errorProp.GetString();
                // Quietly return null for these known cases to let the UI prompt for consent
                if (error == "MSAL_INTERACTION_REQUIRED" || 
                    error == "MSAL_NO_ACCOUNTS" || 
                    error == "MSAL_WRONG_TENANT")
                {
                    return null;
                }
                Console.WriteLine($"DEBUG: Silent JS token acquisition failed: {error}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DEBUG: JS interop error during silent acquisition: {ex.Message}");
        }
        return null;
    }

    public async Task<TokenCredential?> GetTokenCredentialAsync()
    {
        var tenantId = await GetCurrentTenantIdAsync();
        
        // If specific tenant, try JS
        if (!string.IsNullOrEmpty(tenantId))
        {
             var jsToken = await TryGetTokenFromJsAsync("https://management.azure.com/user_impersonation", tenantId);
             if (!string.IsNullOrEmpty(jsToken))
             {
                 return new AccessTokenCredential(jsToken);
             }
             return null;
        }

        return await GetHomeTokenCredentialAsync();
    }

    public async Task<TokenCredential?> GetHomeTokenCredentialAsync()
    {
        try
        {
            var result = await tokenProvider.RequestAccessToken(new AccessTokenRequestOptions
            {
                Scopes = ["https://management.azure.com/user_impersonation"]
            });

            if (result.TryGetToken(out var token))
            {
                return new AccessTokenCredential(token.Value, token.Expires);
            }

            if (result.Status == AccessTokenResultStatus.RequiresRedirect)
            {
                // If the default provider fails, we might need a refresh. 
                // We don't auto-redirect here to avoid loops, let the UI handle it.
                Console.WriteLine("DEBUG: Home token requires redirect.");
            }
            return null;
        }
        catch { return null; }
    }

    public async Task<string?> GetServiceBusTokenAsync()
    {
        var tenantId = await GetCurrentTenantIdAsync();

        if (!string.IsNullOrEmpty(tenantId))
        {
             return await TryGetTokenFromJsAsync("https://servicebus.azure.net/user_impersonation", tenantId);
        }

        var result = await tokenProvider.RequestAccessToken(new AccessTokenRequestOptions
        {
            Scopes = ["https://servicebus.azure.net/user_impersonation"]
        });

        if (result.TryGetToken(out var token))
        {
            return token.Value;
        }
        return null;
    }

    public async Task<bool> AcquireTokenPopupAsync(string scope, string? tenantId, string? loginHint = null)
    {
        try
        {
            await EnsureMsalConfigAsync();
            var result = await jsRuntime.InvokeAsync<JsonElement>("msalHelper.acquireTokenPopup", scope, tenantId, loginHint);
            return result.TryGetProperty("success", out var success) && success.GetBoolean();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: Popup failed: {ex.Message}");
            return false;
        }
    }

    public Task SignInAsync()
    {
        navigationManager.NavigateTo("authentication/login");
        return Task.CompletedTask;
    }

    public Task SignOutAsync()
    {
        navigationManager.NavigateToLogout("authentication/logout");
        return Task.CompletedTask;
    }

    public async Task<string?> GetUserNameAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        return authState.User.Identity?.Name;
    }
    
    public string? GetUserName() => null;

    public async Task ClearMsalCacheAsync()
    {
        try { await jsRuntime.InvokeVoidAsync("msalHelper.clearCache"); } catch { }
    }
}

internal sealed class AccessTokenCredential(string token, DateTimeOffset? expiresOn = null) : TokenCredential
{
    private readonly DateTimeOffset _expiresOn = expiresOn ?? DateTimeOffset.UtcNow.AddHours(1);
    public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
        => new Azure.Core.AccessToken(token, _expiresOn);
    public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
        => new ValueTask<Azure.Core.AccessToken>(new Azure.Core.AccessToken(token, _expiresOn));
}
