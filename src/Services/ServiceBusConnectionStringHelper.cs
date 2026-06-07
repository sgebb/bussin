using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Bussin.Services;

public static class ServiceBusConnectionStringHelper
{
    public static string GenerateSasToken(string connectionString, string entityPath, TimeSpan ttl)
    {
        var (endpoint, keyName, key, defaultEntityPath) = ParseConnectionString(connectionString);
        
        // Use default entity path from connection string if specified, otherwise the requested entity path
        var activeEntityPath = !string.IsNullOrEmpty(defaultEntityPath) ? defaultEntityPath : entityPath;
        
        // Extract host
        var uriStr = endpoint;
        if (!uriStr.StartsWith("sb://", StringComparison.OrdinalIgnoreCase) && 
            !uriStr.StartsWith("amqp://", StringComparison.OrdinalIgnoreCase) && 
            !uriStr.StartsWith("amqps://", StringComparison.OrdinalIgnoreCase))
        {
            uriStr = "sb://" + uriStr;
        }
        
        // Normalize amqp/amqps protocols to sb protocol for the resource URI
        if (uriStr.StartsWith("amqp://", StringComparison.OrdinalIgnoreCase))
        {
            uriStr = "sb://" + uriStr.Substring(7);
        }
        else if (uriStr.StartsWith("amqps://", StringComparison.OrdinalIgnoreCase))
        {
            uriStr = "sb://" + uriStr.Substring(8);
        }

        var uri = new Uri(uriStr);
        var hostname = uri.Host;
        
        // Build resource URI: sb://<hostname>/<entityPath>
        var resourceUri = $"sb://{hostname}/{activeEntityPath.TrimStart('/')}";
        
        var expiry = ((int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds + (int)ttl.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        
        string stringToSign = Uri.EscapeDataString(resourceUri) + "\n" + expiry;
        
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
        
        return string.Format(CultureInfo.InvariantCulture, 
            "SharedAccessSignature sr={0}&sig={1}&se={2}&skn={3}", 
            Uri.EscapeDataString(resourceUri), 
            Uri.EscapeDataString(signature), 
            expiry, 
            keyName);
    }

    public static (string Endpoint, string KeyName, string Key, string? EntityPath) ParseConnectionString(string connectionString)
    {
        connectionString = connectionString.Trim();

        // 1. Check if it is an AMQP URI (e.g. amqps://RootManageSharedAccessKey:key@host:port)
        if (connectionString.StartsWith("amqp://", StringComparison.OrdinalIgnoreCase) || 
            connectionString.StartsWith("amqps://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(connectionString);
                var userInfo = uri.UserInfo;
                if (string.IsNullOrEmpty(userInfo) || !userInfo.Contains(':'))
                {
                    throw new ArgumentException("AMQP URI must contain username:password authentication details.");
                }
                
                var userParts = userInfo.Split(':', 2);
                var keyName = Uri.UnescapeDataString(userParts[0]);
                var key = Uri.UnescapeDataString(userParts[1]);
                
                // Construct endpoint
                var host = uri.Authority; // includes host and port if specified
                var endpoint = $"sb://{host}/";
                
                var path = uri.AbsolutePath.Trim('/');
                string? entityPath = string.IsNullOrEmpty(path) ? null : path;
                
                return (endpoint, keyName, key, entityPath);
            }
            catch (Exception ex) when (!(ex is ArgumentException))
            {
                throw new ArgumentException($"Failed to parse AMQP URI: {ex.Message}");
            }
        }

        // 2. Standard Service Bus Connection String (e.g. Endpoint=sb://...;SharedAccessKeyName=...;SharedAccessKey=...)
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        string endpointVal = "";
        string keyNameVal = "";
        string keyVal = "";
        string? entityPathVal = null;
        
        foreach (var part in parts)
        {
            var kvp = part.Split('=', 2);
            if (kvp.Length == 2)
            {
                var name = kvp[0].Trim();
                var val = kvp[1].Trim();
                
                if (name.Equals("Endpoint", StringComparison.OrdinalIgnoreCase))
                {
                    endpointVal = val;
                }
                else if (name.Equals("SharedAccessKeyName", StringComparison.OrdinalIgnoreCase))
                {
                    keyNameVal = val;
                }
                else if (name.Equals("SharedAccessKey", StringComparison.OrdinalIgnoreCase))
                {
                    keyVal = val;
                }
                else if (name.Equals("EntityPath", StringComparison.OrdinalIgnoreCase))
                {
                    entityPathVal = val;
                }
            }
        }
        
        if (string.IsNullOrEmpty(endpointVal) || string.IsNullOrEmpty(keyNameVal) || string.IsNullOrEmpty(keyVal))
        {
            throw new ArgumentException("Invalid connection string. Must contain Endpoint, SharedAccessKeyName, and SharedAccessKey.");
        }
        
        return (endpointVal, keyNameVal, keyVal, entityPathVal);
    }
}
