using System.Diagnostics;
using ZCL.API;
using ZCM.Notifications;

namespace ZCM.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly Config _config;

    public SettingsPage()
    {
        InitializeComponent();

        _config = Config.Instance;

        BindingContext = _config;
    }

    private async void SaveButton_Clicked(object sender, EventArgs e)
    {
        _ = TransientNotificationService.ShowAsync(
            "Configuration saved successfully.",
            NotificationSeverity.Success,
            2000);

        await Navigation.PopModalAsync(false);
    }


    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync(false);
    }

    private async void OnBackdropTapped(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync(false);
    }








}