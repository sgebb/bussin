using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Bussin.Backend.Services;

public interface ITokenValidationService
{
    Task<ClaimsPrincipal?> ValidateTokenAsync(string token);
}

public class TokenValidationService : ITokenValidationService
{
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configManager;
    private const string ClientId = "36145d65-2256-48e6-a5f6-ae8fde23c103"; // bussin-appreg Client ID
    private const string Authority = "https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration";

    public TokenValidationService()
    {
        _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            Authority,
            new OpenIdConnectConfigurationRetriever());
    }

    public async Task<ClaimsPrincipal?> ValidateTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        try
        {
            var config = await _configManager.GetConfigurationAsync();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                // Multi-tenant validation: verify the token is signed by Entra ID (supports both v1.0 and v2.0 tokens)
                IssuerValidator = (issuer, securityToken, validationParams) =>
                {
                    if (issuer.StartsWith("https://login.microsoftonline.com/") || 
                        issuer.StartsWith("https://sts.windows.net/"))
                    {
                        return issuer;
                    }
                    throw new SecurityTokenInvalidIssuerException("Invalid token issuer.");
                },
                ValidateAudience = true,
                ValidAudiences = new[] { ClientId, $"api://{ClientId}" },
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = config.SigningKeys,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch (Exception)
        {
            return null; // Invalid token
        }
    }
}
