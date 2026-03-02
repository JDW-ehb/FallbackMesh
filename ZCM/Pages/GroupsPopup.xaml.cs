using System.Security.Cryptography;
using ZCM.ViewModels;

namespace ZCM.Pages;

public partial class GroupsPopup : ContentPage
{
    private readonly SettingsViewModel _vm;

    public GroupsPopup(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    private void OnCloseClicked(object sender, EventArgs e)
        => SafeClose();

    private void OnBackdropTapped(object sender, EventArgs e)
        => SafeClose();

    // -------------------------------------------------
    // Add Group
    // -------------------------------------------------

    private void OnAddGroupClicked(object sender, EventArgs e)
    {
        var hex = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        _vm.Groups.Add(new TrustGroupDraftItem
        {
            Id = Guid.NewGuid(),
            Name = $"Group {_vm.Groups.Count + 1}",
            SecretHex = hex,
            IsEnabled = true
        });
    }

    // -------------------------------------------------
    // Copy Secret
    // -------------------------------------------------

    private async void OnCopySecretClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn)
            return;

        if (btn.BindingContext is not TrustGroupDraftItem item)
            return;

        await Clipboard.Default.SetTextAsync(item.SecretHex);
    }

    // -------------------------------------------------
    // Regenerate Secret
    // -------------------------------------------------

    private void OnRegenerateSecretClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn)
            return;

        if (btn.BindingContext is not TrustGroupDraftItem item)
            return;

        item.SecretHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    // -------------------------------------------------
    // Toggle Show / Hide Secret
    // -------------------------------------------------

    private void OnToggleSecretClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn)
            return;

        if (btn.Parent is not Grid grid)
            return;

        var entry = grid.Children.OfType<Entry>().FirstOrDefault();
        if (entry == null)
            return;

        entry.IsPassword = !entry.IsPassword;
    }


    private void SafeClose()
    {
        Dispatcher.Dispatch(async () =>
        {
            try
            {
                if (Navigation?.ModalStack?.Count > 0)
                    await Navigation.PopModalAsync(false);
            }
            catch
            {
                // swallow WinUI handler race
            }
        });
    }
}