namespace Bussin.Services;

/// <summary>
/// Helper class for formatting Azure Service Bus permission-related errors into user-friendly messages.
/// </summary>
public static class PermissionErrorHelper
{
    /// <summary>
    /// Checks if the error message is a permission-related error.
    /// </summary>
    public static bool IsPermissionError(string errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
            return false;

        var lowerError = errorMessage.ToLowerInvariant();
        return ContainsListenError(lowerError) ||
               ContainsSendError(lowerError) ||
               ContainsManageError(lowerError) ||
               Contains401Error(lowerError);
    }

    /// <summary>
    /// Gets the type of permission error (Listen, Send, Manage, or Generic).
    /// </summary>
    public static string GetPermissionType(string errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
            return "Unknown";

        var lowerError = errorMessage.ToLowerInvariant();
        
        if (ContainsListenError(lowerError))
            return "Listen";
        if (ContainsSendError(lowerError))
            return "Send";
        if (ContainsManageError(lowerError))
            return "Manage";
        
        return "Generic";
    }

    /// <summary>
    /// Formats an error message with helpful guidance when it's related to permissions.
    /// Returns a user-friendly summary without the technical details.
    /// </summary>
    public static string FormatError(string errorMessage, string operation = "access")
    {
        if (string.IsNullOrEmpty(errorMessage))
            return errorMessage;

        var lowerError = errorMessage.ToLowerInvariant();

        // Check for Listen claim errors (peek, receive, subscribe)
        if (ContainsListenError(lowerError))
        {
            return "Permission denied: You don't have 'Listen' access to this entity.";
        }

        // Check for Send claim errors
        if (ContainsSendError(lowerError))
        {
            return "Permission denied: You don't have 'Send' access to this entity.";
        }

        // Check for Manage claim errors
        if (ContainsManageError(lowerError))
        {
            return "Permission denied: You don't have 'Manage' access to this entity.";
        }

        // Generic 401/Unauthorized
        if (Contains401Error(lowerError))
        {
            return $"Permission denied: You don't have the required permissions to {operation} this entity.";
        }

        // WebSocket connection failures (often permission-related too)
        if (lowerError.Contains("websocket"))
        {
            return "WebSocket connection failed. This could be due to token expiration (try refreshing) or network issues.";
        }

        // Clean up common error prefixes for non-permission errors
        return CleanErrorMessage(errorMessage);
    }

    /// <summary>
    /// Gets the recommended RBAC roles for the given permission type.
    /// </summary>
    public static string[] GetRecommendedRoles(string permissionType)
    {
        return permissionType switch
        {
            "Listen" => new[] { "Azure Service Bus Data Receiver", "Azure Service Bus Data Owner" },
            "Send" => new[] { "Azure Service Bus Data Sender", "Azure Service Bus Data Owner" },
            "Manage" => new[] { "Azure Service Bus Data Owner" },
            _ => new[] { "Azure Service Bus Data Receiver", "Azure Service Bus Data Sender", "Azure Service Bus Data Owner" }
        };
    }

    /// <summary>
    /// Gets help text for using Shared Access Policies.
    /// </summary>
    public static string GetSasPolicyHelp(string permissionType)
    {
        var claim = permissionType switch
        {
            "Listen" => "'Listen'",
            "Send" => "'Send'",
            "Manage" => "'Manage'",
            _ => "the required permissions"
        };
        
        return $"If using Shared Access Policies, ensure your policy has {claim} enabled.";
    }

    /// <summary>
    /// Cleans up common error message prefixes and duplication.
    /// </summary>
    private static string CleanErrorMessage(string errorMessage)
    {
        // Remove duplicate prefixes
        errorMessage = errorMessage.Replace("Peek failed: Peek failed:", "Peek failed:");
        errorMessage = errorMessage.Replace("Purge failed: Purge failed:", "Purge failed:");
        errorMessage = errorMessage.Replace("Send failed: Send failed:", "Send failed:");
        
        return errorMessage;
    }

    private static bool ContainsListenError(string lowerError)
    {
        return (lowerError.Contains("listen") && lowerError.Contains("claim")) ||
               (lowerError.Contains("receive") && lowerError.Contains("unauthorized"));
    }

    private static bool ContainsSendError(string lowerError)
    {
        return (lowerError.Contains("send") && lowerError.Contains("claim")) ||
               (lowerError.Contains("send") && lowerError.Contains("unauthorized"));
    }

    private static bool ContainsManageError(string lowerError)
    {
        return (lowerError.Contains("manage") && lowerError.Contains("claim")) ||
               (lowerError.Contains("manage") && lowerError.Contains("unauthorized"));
    }

    private static bool Contains401Error(string lowerError)
    {
        return lowerError.Contains("401") || 
               lowerError.Contains("unauthorized") || 
               lowerError.Contains("claim");
    }

    /// <summary>
    /// Formats an ARM API error with helpful guidance for namespace access.
    /// </summary>
    public static string FormatArmError(string errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
            return errorMessage;

        var lowerError = errorMessage.ToLowerInvariant();

        if (lowerError.Contains("authorizationfailed") || lowerError.Contains("authorization failed"))
        {
            return "Authorization failed: You don't have permission to view Service Bus namespaces.";
        }

        if (lowerError.Contains("subscription") && (lowerError.Contains("not found") || lowerError.Contains("invalid")))
        {
            return "Subscription not accessible: The Azure subscription may not be available to your account.";
        }

        return errorMessage;
    }
}
