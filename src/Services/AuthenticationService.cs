using Azure.Core;
using Bussin.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using System.Security.Claims;
using System.Text.Json;

namespace Bussin.Services;

/// <summary>
/// Sign-in state and tokens, backed by the single MSAL instance in wwwroot/js/msal-helper.js.
/// Also serves as the app's <see cref="AuthenticationStateProvider"/>: the user counts as signed in while an
/// account is cached, even if its tokens have stopped working; <see cref="CheckAccessAsync"/> tells those apart.
/// </summary>
public sealed class AuthenticationService(
    IPreferencesService preferencesService,
    IJSRuntime jsRuntime,
    IConfiguration configuration) : AuthenticationStateProvider, IAuthenticationService
{
    private const string ArmScope = "https://management.azure.com/user_impersonation";
    private const string ServiceBusScope = "https://servicebus.azure.net/user_impersonation";

    private Task<AuthenticationState>? _authenticationState;

    public bool IsDemoMode => false;

    // Every page load starts here, so this is also where a returning sign-in redirect is processed.
    // Sign-in, sign-out and granting access all reload the page, so the state never changes within a load.
    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        _authenticationState ??= InitializeAsync();

    private async Task<AuthenticationState> InitializeAsync()
    {
        var claims = await jsRuntime.InvokeAsync<JsonElement?>("msalHelper.init", new
        {
            clientId = configuration["AzureAd:ClientId"],
            authority = configuration["AzureAd:Authority"]
        });
        return new AuthenticationState(new ClaimsPrincipal(ToIdentity(claims)));
    }

    private static ClaimsIdentity ToIdentity(JsonElement? idTokenClaims)
    {
        if (idTokenClaims is not { ValueKind: JsonValueKind.Object } claims)
        {
            return new ClaimsIdentity();
        }

        var list = claims.EnumerateObject().SelectMany(claim => claim.Value.ValueKind == JsonValueKind.Array
            ? claim.Value.EnumerateArray().Select(value => new Claim(claim.Name, value.ToString()))
            : [new Claim(claim.Name, claim.Value.ToString())]);
        return new ClaimsIdentity(list, authenticationType: "msal", nameType: "name", roleType: "roles");
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        var state = await GetAuthenticationStateAsync();
        return state.User.Identity?.IsAuthenticated ?? false;
    }

    public async Task SignInAsync()
    {
        await GetAuthenticationStateAsync();
        await jsRuntime.InvokeVoidAsync("msalHelper.loginRedirect", ArmScope, new[] { ServiceBusScope });
    }

    public async Task SignOutAsync()
    {
        // The selected guest tenant lives in preferences, not the MSAL cache, so it would otherwise
        // survive sign-out and drop the next sign-in straight back into that tenant's consent flow.
        await SetTenantAsync(null);
        await GetAuthenticationStateAsync();
        await jsRuntime.InvokeVoidAsync("msalHelper.logoutRedirect");
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

    public async Task<TokenCredential?> GetTokenCredentialAsync() =>
        ToCredential(await AcquireTokenSilentAsync(ArmScope, await GetCurrentTenantIdAsync()));

    public async Task<TokenCredential?> GetHomeTokenCredentialAsync() =>
        ToCredential(await AcquireTokenSilentAsync(ArmScope, tenantId: null));

    public async Task<string?> GetServiceBusTokenAsync() =>
        (await AcquireTokenSilentAsync(ServiceBusScope, await GetCurrentTenantIdAsync())).Token;

    public async Task<AccessStatus> CheckAccessAsync()
    {
        var tenantId = await GetCurrentTenantIdAsync();
        var management = await AcquireTokenSilentAsync(ArmScope, tenantId);
        var serviceBus = await AcquireTokenSilentAsync(ServiceBusScope, tenantId);
        return new AccessStatus(management.AccessProblem, serviceBus.AccessProblem, management.Message ?? serviceBus.Message);
    }

    public async Task<string?> GrantAccessAsync()
    {
        // Nothing slow may happen before the popup opens, or the browser no longer sees it as a response
        // to the click and blocks it. Both awaits here complete from memory.
        await GetAuthenticationStateAsync();
        var tenantId = await GetCurrentTenantIdAsync();
        try
        {
            return await jsRuntime.InvokeAsync<string?>("msalHelper.acquireTokenPopup", ArmScope, new[] { ServiceBusScope }, tenantId);
        }
        catch (JSException ex)
        {
            return ex.Message;
        }
    }

    private async Task<SilentTokenResult> AcquireTokenSilentAsync(string scope, string? tenantId)
    {
        await GetAuthenticationStateAsync();
        try
        {
            var result = await jsRuntime.InvokeAsync<SilentTokenResult>("msalHelper.acquireTokenSilent", scope, tenantId);
            if (result.Token is null)
            {
                Console.WriteLine($"Silent token for {scope} failed ({result.Problem}): {result.Message}");
            }
            return result;
        }
        catch (JSException ex)
        {
            return new SilentTokenResult(null, null, nameof(AccessProblem.Failed), ex.Message);
        }
    }

    private static TokenCredential? ToCredential(SilentTokenResult result) =>
        result.Token is null ? null : new AccessTokenCredential(result.Token, result.ExpiresOn);

    // Shape returned by msalHelper.acquireTokenSilent: either Token, or Problem (an AccessProblem name) and Message.
    private sealed record SilentTokenResult(string? Token, DateTimeOffset? ExpiresOn, string? Problem, string? Message)
    {
        public AccessProblem AccessProblem => Token is not null ? AccessProblem.None
            : Enum.TryParse<AccessProblem>(Problem, out var problem) ? problem
            : AccessProblem.Failed;
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
