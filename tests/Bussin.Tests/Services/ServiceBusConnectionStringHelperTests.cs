using System.Security.Cryptography;
using System.Text;
using Bussin.Services;
using Xunit;

namespace Bussin.Tests.Services;

public sealed class ServiceBusConnectionStringHelperTests
{
    private const string Key = "c2VjcmV0LWtleQ==";
    private const string ConnectionString =
        $"Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKeyName=Listen;SharedAccessKey={Key}";

    [Fact]
    public void ParsesAStandardConnectionString()
    {
        var (endpoint, keyName, key, entityPath) =
            ServiceBusConnectionStringHelper.ParseConnectionString(ConnectionString + ";EntityPath=orders");

        Assert.Equal("sb://contoso.servicebus.windows.net/", endpoint);
        Assert.Equal("Listen", keyName);
        Assert.Equal(Key, key);
        Assert.Equal("orders", entityPath);
    }

    [Fact]
    public void ParsesAnAmqpUri()
    {
        var (endpoint, keyName, key, entityPath) =
            ServiceBusConnectionStringHelper.ParseConnectionString("amqps://Listen:a%2Fb@localhost:5671/orders");

        Assert.Equal("sb://localhost:5671/", endpoint);
        Assert.Equal("Listen", keyName);
        Assert.Equal("a/b", key);
        Assert.Equal("orders", entityPath);
    }

    [Fact]
    public void RejectsAConnectionStringWithoutAKey()
    {
        Assert.Throws<ArgumentException>(() =>
            ServiceBusConnectionStringHelper.ParseConnectionString("Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKeyName=Listen"));
    }

    [Fact]
    public void SignsTheRequestedEntityWithTheKey()
    {
        var token = ServiceBusConnectionStringHelper.GenerateSasToken(ConnectionString, "orders/$DeadLetterQueue", TimeSpan.FromHours(1));

        var fields = ParseToken(token);
        Assert.Equal("sb://contoso.servicebus.windows.net/orders/$DeadLetterQueue", Uri.UnescapeDataString(fields["sr"]));
        Assert.Equal("Listen", fields["skn"]);
        Assert.Equal(ExpectedSignature(fields["sr"], fields["se"]), Uri.UnescapeDataString(fields["sig"]));

        var expiry = DateTimeOffset.FromUnixTimeSeconds(long.Parse(fields["se"]));
        Assert.InRange(expiry, DateTimeOffset.UtcNow.AddMinutes(59), DateTimeOffset.UtcNow.AddMinutes(61));
    }

    [Fact]
    public void AnEntityScopedConnectionStringSignsItsOwnEntity()
    {
        var token = ServiceBusConnectionStringHelper.GenerateSasToken(ConnectionString + ";EntityPath=orders", "something-else", TimeSpan.FromHours(1));

        Assert.Equal("sb://contoso.servicebus.windows.net/orders", Uri.UnescapeDataString(ParseToken(token)["sr"]));
    }

    private static Dictionary<string, string> ParseToken(string token)
    {
        Assert.StartsWith("SharedAccessSignature ", token);
        return token["SharedAccessSignature ".Length..]
            .Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(kv => kv[0], kv => kv[1]);
    }

    // The signature Service Bus checks: HMAC-SHA256 over "<url-encoded resource>\n<expiry>"
    private static string ExpectedSignature(string encodedResource, string expiry)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Key));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{encodedResource}\n{expiry}")));
    }
}
