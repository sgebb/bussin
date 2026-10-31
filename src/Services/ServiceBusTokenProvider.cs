namespace Bussin.Services;

/// <summary>
/// Gets the token the client-js AMQP calls authenticate with: a SAS token for namespaces added by
/// connection string, otherwise the signed-in user's Service Bus token.
/// </summary>
public sealed class ServiceBusTokenProvider(NavigationStateService navState, IAuthenticationService authService)
{
    public async Task<string> GetTokenAsync(string namespaceName, string entityPath)
    {
        await navState.InitializeAsync();
        var connection = navState.GetNamespaceConnection(namespaceName);
        if (!string.IsNullOrEmpty(connection?.ConnectionString))
        {
            return ServiceBusConnectionStringHelper.GenerateSasToken(connection.ConnectionString, entityPath, TimeSpan.FromHours(2));
        }

        var token = await authService.GetServiceBusTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException(
                "Service Bus token not available. You may need to sign out and sign in again to consent to the Service Bus scope.");
        }
        return token;
    }
}
