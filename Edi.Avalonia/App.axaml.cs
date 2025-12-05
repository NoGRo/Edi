using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Edi.Avalonia.Views;
using Edi.Core;

namespace Edi.Avalonia;

public partial class App : Application
{
    public static IEdi Edi { get; private set; } = null!;
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    public App(IEdi edi, IServiceProvider serviceProvider)
    {
        Edi = edi;
        ServiceProvider = serviceProvider;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}