using Bussin.Models;

namespace Bussin.Services;

public static class MessageHelpers
{
    public static bool IsScheduledMessage(ServiceBusMessage message)
    {
        if (!message.ScheduledEnqueueTime.HasValue || message.ScheduledEnqueueTime.Value == 0)
            return false;
        var scheduledTime = DateTimeOffset.FromUnixTimeMilliseconds(message.ScheduledEnqueueTime.Value);
        return scheduledTime > DateTimeOffset.UtcNow;
    }
}
