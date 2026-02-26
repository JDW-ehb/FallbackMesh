using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using ZCM.Controls;

namespace ZCM;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();



        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Console.WriteLine("UNHANDLED EXCEPTION:");
            Console.WriteLine(e.ExceptionObject);
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Console.WriteLine("UNOBSERVED TASK EXCEPTION:");
            Console.WriteLine(e.Exception);
            e.SetObserved();
        };

        // Enable text selection for SelectableLabel
        LabelHandler.Mapper.AppendToMapping(
            "SelectableLabelMapping",
            (handler, view) =>
            {
                if (view is SelectableLabel)
                {
#if WINDOWS
                    handler.PlatformView.IsTextSelectionEnabled = true;
#elif ANDROID
                    handler.PlatformView.SetTextIsSelectable(true);
#endif
                }
            });

    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }
}
