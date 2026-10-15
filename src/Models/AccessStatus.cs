namespace Bussin.Models;

/// <summary>Why a token could not be acquired silently. Names match msal-helper.js.</summary>
public enum AccessProblem
{
    None,
    /// <summary>The sign-in is no longer valid, e.g. after a password change. Signing in again fixes it.</summary>
    SignInRequired,
    /// <summary>Nobody has consented to this resource for the app in this tenant yet.</summary>
    ConsentRequired,
    /// <summary>Anything else; <see cref="AccessStatus.ErrorMessage"/> says what.</summary>
    Failed
}

/// <summary>Result of silently requesting both tokens the app needs in the current tenant.</summary>
public sealed record AccessStatus(AccessProblem Management, AccessProblem ServiceBus, string? ErrorMessage)
{
    public bool IsComplete => Management == AccessProblem.None && ServiceBus == AccessProblem.None;

    /// <summary>The one problem to show. Signing in again goes first because the same popup also covers consent.</summary>
    public AccessProblem Problem =>
        new[] { AccessProblem.SignInRequired, AccessProblem.ConsentRequired, AccessProblem.Failed }
            .FirstOrDefault(p => Management == p || ServiceBus == p);
}
