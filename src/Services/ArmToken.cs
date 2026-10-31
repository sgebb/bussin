namespace Bussin.Services;

/// <summary>
/// An Azure Resource Manager bearer token acquired by MSAL.js, sent as-is on management API calls.
/// </summary>
public sealed record ArmToken(string Token, DateTimeOffset ExpiresOn);
