using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;

namespace ZCM.Notifications;

public static class TransientNotificationService
{
    public static async Task ShowAsync(
        string message,
        NotificationSeverity severity = NotificationSeverity.Info,
        int durationMs = 5000)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Page is not Page rootPage)
                return;

            Page? page = rootPage switch
            {
                Shell shell => shell.CurrentPage,
                _ => rootPage
            };

            if (page is NavigationPage navPage)
                page = navPage.CurrentPage;

            if (page is not ContentPage contentPage)
                return;

            if (contentPage.Content is not Layout layout)
                return;
            var background = severity switch
            {
                NotificationSeverity.Success => Color.FromArgb("#27AE60"),
                NotificationSeverity.Warning => Color.FromArgb("#E67E22"),
                NotificationSeverity.Error => Color.FromArgb("#C0392B"),
                _ => Color.FromArgb("#2A2A2A")
            };

            var container = new Grid
            {
                VerticalOptions = LayoutOptions.End,
                HorizontalOptions = LayoutOptions.End,
                Margin = new Thickness(0, 0, 24, 24),
                InputTransparent = true
            };

            var label = new Label
            {
                Text = message,
                BackgroundColor = background,
                TextColor = Colors.White,
                Padding = new Thickness(18, 10),
                Opacity = 0
            };

            container.Children.Add(label);
            layout.Children.Add(container);

            await label.FadeTo(1, 200);
            await Task.Delay(durationMs);
            await label.FadeTo(0, 200);

            layout.Children.Remove(container);
        });
    }
}