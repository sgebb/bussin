using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Bussin.Services.Demo;

public class DemoAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly AuthenticationState _anonymousStub;
    private readonly AuthenticationState _authenticatedStub;

    public DemoAuthenticationStateProvider()
    {
        _anonymousStub = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "Demo User"),
            new Claim("oid", "0000-0000-0000-0000"),
            new Claim("name", "Demo User")
        }, "DemoAuth");

        _authenticatedStub = new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        return Task.FromResult(_authenticatedStub);
    }
}
