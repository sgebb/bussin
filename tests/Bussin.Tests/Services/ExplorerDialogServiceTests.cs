using Bussin.Services;
using Xunit;

namespace Bussin.Tests.Services;

public sealed class ExplorerDialogServiceTests
{
    [Fact]
    public async Task CancelledResubmitDialog_DoesNotLeakIntoNextConfirmDialog()
    {
        var confirmModal = new ConfirmModalService();
        using var dialogs = new ExplorerDialogService(confirmModal);

        dialogs.IsResubmitModal = true;
        dialogs.IsMoveToDLQModal = true;
        confirmModal.Show("Confirm Resubmit", "Resubmit?", null, "Resubmit", "btn-primary", () => Task.CompletedTask);
        Assert.True(dialogs.IsResubmitModal);

        await confirmModal.CancelAsync();
        confirmModal.Show("Confirm Purge", "Purge?", null, "Purge", "btn-danger", () => Task.CompletedTask);

        Assert.False(dialogs.IsResubmitModal);
        Assert.False(dialogs.IsMoveToDLQModal);
    }
}
