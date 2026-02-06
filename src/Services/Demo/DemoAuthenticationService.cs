using Azure.Core;
using Bussin.Models;
using Bussin.Services;

namespace Bussin.Services.Demo;

public class DemoAuthenticationService : IAuthenticationService
{
    public Task<bool> IsAuthenticatedAsync()
    {
        return Task.FromResult(true);
    }

    public Task<TokenCredential?> GetTokenCredentialAsync()
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
}
