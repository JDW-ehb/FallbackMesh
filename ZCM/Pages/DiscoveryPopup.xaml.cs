using ZCM;

namespace ZCM.Pages;

public partial class DiscoveryPopup : ContentPage
{
    public DiscoveryPopup(MainPage main)
    {
        InitializeComponent();
        BindingContext = main;
    }

    private async void OnCloseClicked(object sender, EventArgs e)
        => await Navigation.PopModalAsync(false);

    private async void OnBackdropTapped(object sender, EventArgs e)
        => await Navigation.PopModalAsync(false);

    private async void OnPeerTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not MainPage.PeerNodeCard card)
            return;

        await Navigation.PushModalAsync(
            new PeerDetailsPage { Card = card },
            false);
    }
}