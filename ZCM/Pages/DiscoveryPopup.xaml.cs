using ZCL.Models;
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

    private async void OnMessagingPageTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not PeerNode peer)
            return;

        // close the popup/modal first, then Shell navigate
        await Navigation.PopModalAsync(false);

        await Shell.Current.GoToAsync(nameof(MessagingPage),
            new Dictionary<string, object> { { "Peer", peer } });
    }
}