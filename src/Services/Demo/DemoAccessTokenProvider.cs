using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace Bussin.Services.Demo;

public class DemoAccessTokenProvider : IAccessTokenProvider
{
    public ValueTask<AccessTokenResult> RequestAccessToken()
    {
        return RequestAccessToken(new AccessTokenRequestOptions());
    }

    public ValueTask<AccessTokenResult> RequestAccessToken(AccessTokenRequestOptions options)
    {
        var token = new AccessToken
        {
            Value = "demo-access-token",
            Expires = DateTimeOffset.UtcNow.AddHours(1),
            GrantedScopes = (options.Scopes ?? Array.Empty<string>()).ToArray()
        };
        return ValueTask.FromResult(new AccessTokenResult(AccessTokenResultStatus.Success, token, null, null));
    }
}
