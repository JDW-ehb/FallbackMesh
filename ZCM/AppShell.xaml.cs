using ZCM.Notifications;
using ZCM.Pages;

namespace ZCM
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute(nameof(MessagingPage), typeof(MessagingPage));
            Routing.RegisterRoute(nameof(LLMChatPage), typeof(LLMChatPage));
            Routing.RegisterRoute(nameof(FileSharingPage), typeof(FileSharingPage));
        }

        private async void DashboardButton_Clicked(object sender, EventArgs e)
        {
            FlyoutIsPresented = false;
            await GoToAsync("//MainPage");
        }

        private async void MessagingButton_Clicked(object sender, EventArgs e)
        {
            FlyoutIsPresented = false;
            await GoToAsync(nameof(MessagingPage));
        }

        private async void LlmButton_Clicked(object sender, EventArgs e)
        {
            FlyoutIsPresented = false;
            await GoToAsync(nameof(LLMChatPage));
        }

        private async void ShareButton_Clicked(object sender, EventArgs e)
        {
            FlyoutIsPresented = false;
            await GoToAsync(nameof(FileSharingPage));
        }

        private async void SettingsButton_Clicked(object sender, EventArgs e)
        {
            FlyoutIsPresented = false;
            await Navigation.PushModalAsync(new SettingsPage(), false);
        }

        private async void DiscoveryButton_Clicked(object sender, EventArgs e)
        {
            FlyoutIsPresented = false;

            // DiscoveryPopup expects a MainPage instance.
            // If you're already on MainPage, reuse it; otherwise navigate to it first.
            if (CurrentPage is MainPage main)
            {
                await main.Navigation.PushModalAsync(new DiscoveryPopup(main), false);
                return;
            }

            await GoToAsync("//MainPage");

            if (CurrentPage is MainPage mainAfterNav)
                await mainAfterNav.Navigation.PushModalAsync(new DiscoveryPopup(mainAfterNav), false);
        }

        private void ExitButton_Clicked(object sender, EventArgs e)
        {
#if ANDROID || IOS || MACCATALYST || WINDOWS
            Application.Current?.Quit();
#endif
        }
    }
}
