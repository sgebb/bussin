using System;
using System.Threading.Tasks;
using Bussin.Components;

namespace Bussin.Services;

/// <summary>
/// Scoped service coordinating visibility states for all modals, dialogs,
/// and bridging the confirmation modal service calls to the UI.
/// </summary>
public sealed class ExplorerDialogService : IDisposable
{
    private readonly IConfirmModalService _confirmModal;

    public event Action? OnChange;
    private void NotifyStateChanged() => OnChange?.Invoke();

    // Dialog Visibility State
    private bool _showSendMessageModal;
    public bool ShowSendMessageModal
    {
        get => _showSendMessageModal;
        set { _showSendMessageModal = value; NotifyStateChanged(); }
    }

    private bool _showMessageDetailModal;
    public bool ShowMessageDetailModal
    {
        get => _showMessageDetailModal;
        set { _showMessageDetailModal = value; NotifyStateChanged(); }
    }

    private bool _showPeekLockModal;
    public bool ShowPeekLockModal
    {
        get => _showPeekLockModal;
        set { _showPeekLockModal = value; NotifyStateChanged(); }
    }

    private bool _showMonitorModal;
    public bool ShowMonitorModal
    {
        get => _showMonitorModal;
        set { _showMonitorModal = value; NotifyStateChanged(); }
    }

    private bool _showReceiveAndLockModal;
    public bool ShowReceiveAndLockModal
    {
        get => _showReceiveAndLockModal;
        set { _showReceiveAndLockModal = value; NotifyStateChanged(); }
    }

    private bool _showPeekOptionsModal;
    public bool ShowPeekOptionsModal
    {
        get => _showPeekOptionsModal;
        set { _showPeekOptionsModal = value; NotifyStateChanged(); }
    }

    private bool _showStatsModal;
    public bool ShowStatsModal
    {
        get => _showStatsModal;
        set { _showStatsModal = value; NotifyStateChanged(); }
    }

    private bool _showExportOptionsModal;
    public bool ShowExportOptionsModal
    {
        get => _showExportOptionsModal;
        set { _showExportOptionsModal = value; NotifyStateChanged(); }
    }

    private bool _showKeyboardShortcutsModal;
    public bool ShowKeyboardShortcutsModal
    {
        get => _showKeyboardShortcutsModal;
        set { _showKeyboardShortcutsModal = value; NotifyStateChanged(); }
    }

    private bool _showManageRulesModal;
    public bool ShowManageRulesModal
    {
        get => _showManageRulesModal;
        set { _showManageRulesModal = value; NotifyStateChanged(); }
    }

    // Export Options state
    public Bussin.Components.ExportOptionsModal.ExportOptions MessageExportOptions { get; set; } = new();

    // Rename state
    private bool _isEditingDisplayName;
    public bool IsEditingDisplayName
    {
        get => _isEditingDisplayName;
        set { _isEditingDisplayName = value; NotifyStateChanged(); }
    }

    public string EditDisplayName { get; set; } = "";

    // Resubmit Modal state
    public bool ResubmitRemoveFromDLQ { get; set; } = true;
    public bool IsResubmitModal { get; set; }

    // Move to DLQ Modal state
    public bool IsMoveToDLQModal { get; set; }
    public string MoveToDLQReason { get; set; } = "Manual move to DLQ";
    public string MoveToDLQErrorDescription { get; set; } = "Moved by user";



    // ConfirmModal Bridge
    public bool ConfirmModalVisible => _confirmModal.IsVisible;
    public string ConfirmModalTitle => _confirmModal.Title;
    public string ConfirmModalMessage => _confirmModal.Message;
    public string ConfirmModalDetailMessage => _confirmModal.DetailMessage;
    public string ConfirmModalConfirmButtonText => _confirmModal.ConfirmButtonText;
    public string ConfirmModalConfirmButtonClass => _confirmModal.ConfirmButtonClass;
    public string ConfirmModalAlternativeButtonText => _confirmModal.AlternativeButtonText;
    public string ConfirmModalAlternativeButtonClass => _confirmModal.AlternativeButtonClass;
    
    public bool ConfirmModalIsProcessing
    {
        get => _confirmModal.IsProcessing;
        set { _confirmModal.IsProcessing = value; NotifyStateChanged(); }
    }
    public int? ConfirmModalProgressCount => _confirmModal.ProgressCount;

    public ExplorerDialogService(IConfirmModalService confirmModal)
    {
        _confirmModal = confirmModal;
        _confirmModal.OnChange += NotifyStateChanged;
    }

    public Task OnConfirmModalConfirmAsync() => _confirmModal.ConfirmAsync();
    public Task OnConfirmModalAlternativeConfirmAsync() => _confirmModal.AlternativeConfirmAsync();
    public Task OnConfirmModalCancelAsync() => _confirmModal.CancelAsync();

    public void CloseMessageDetail()
    {
        ShowMessageDetailModal = false;
    }

    public void Dispose()
    {
        _confirmModal.OnChange -= NotifyStateChanged;
    }
}
