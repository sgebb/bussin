using Bussin.Services;
using Xunit;

namespace Bussin.Tests.Services;

public sealed class PermissionErrorHelperTests
{
    private const string IpBlocked =
        "Peek failed: CBS auth failed: 401 - Ip has been prevented to connect to the endpoint.\n" +
        "For more information see:\nVirtual Network service endpoints:\n" +
        "TrackingId:60db63f5, SystemTracker:ns.servicebus.windows.net:q";

    [Fact]
    public void IpFirewallBlock_IsReportedAsNetworkNotPermission()
    {
        Assert.True(PermissionErrorHelper.IsNetworkBlocked(IpBlocked));
        Assert.False(PermissionErrorHelper.IsPermissionError(IpBlocked));
        Assert.StartsWith("Network access denied", PermissionErrorHelper.FormatError(IpBlocked, "peek messages from"));
    }

    [Fact]
    public void MissingListenClaim_IsStillAPermissionError()
    {
        const string error = "Peek failed: CBS auth failed: 401 - Unauthorized access. 'Listen' claim(s) are required to perform this operation.";

        Assert.False(PermissionErrorHelper.IsNetworkBlocked(error));
        Assert.True(PermissionErrorHelper.IsPermissionError(error));
        Assert.Equal("Listen", PermissionErrorHelper.GetPermissionType(error));
    }
}
