using Bussin.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    public async Task<ArmToken?> GetTokenCredentialAsync() =>
        ToCredential(await AcquireTokenSilentAsync(ArmScope, await GetCurrentTenantIdAsync()));

    public async Task<ArmToken?> GetHomeTokenCredentialAsync() =>
        ToCredential(await AcquireTokenSilentAsync(ArmScope, tenantId: null));

    public async Task<string?> GetServiceBusTokenAsync() =>
        TokenOf(await AcquireTokenSilentAsync(ServiceBusScope, await GetCurrentTenantIdAsync()));

    public async Task<AccessStatus> CheckAccessAsync()
    {
        var tenantId = await GetCurrentTenantIdAsync();
        var management = await AcquireTokenSilentAsync(ArmScope, tenantId);
        var serviceBus = await AcquireTokenSilentAsync(ServiceBusScope, tenantId);
        return new AccessStatus(ProblemOf(management), ProblemOf(serviceBus), MessageOf(management) ?? MessageOf(serviceBus));
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
            if (result is TokenProblem problem)
            {
                Console.WriteLine($"Silent token for {scope} failed ({problem.Problem}): {problem.Message}");
            }
            return result;
        }
        catch (Exception ex) when (ex is JSException or JsonException)
        {
            return new TokenProblem(nameof(AccessProblem.Failed), ex.Message);
        }
    }

    private static ArmToken? ToCredential(SilentTokenResult result) => result switch
    {
        TokenAcquired t => new ArmToken(t.Token, t.ExpiresOn ?? DateTimeOffset.UtcNow.AddHours(1)),
        TokenProblem => null,
    };

    private static string? TokenOf(SilentTokenResult result) => result is TokenAcquired t ? t.Token : null;

    private static AccessProblem ProblemOf(SilentTokenResult result) => result switch
    {
        TokenAcquired => AccessProblem.None,
        TokenProblem p => Enum.TryParse<AccessProblem>(p.Problem, out var problem) ? problem : AccessProblem.Failed,
    };

    private static string? MessageOf(SilentTokenResult result) => result is TokenProblem p ? p.Message : null;

    // Shapes returned by msalHelper.acquireTokenSilent: { token, expiresOn } or { problem, message },
    // told apart by which properties are present. Problem is an AccessProblem name.
    private sealed record TokenAcquired(string Token, DateTimeOffset? ExpiresOn);
    private sealed record TokenProblem(string Problem, string? Message);

    [JsonUnion(TypeClassifier = typeof(JsonUnionTypeStructuralClassifier))]
    private union SilentTokenResult(TokenAcquired, TokenProblem);
}
