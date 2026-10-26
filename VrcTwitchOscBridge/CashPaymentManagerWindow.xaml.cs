using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using VrcTwitchOscBridge.Services;
using VrcTwitchOscBridge.ViewModels;

namespace VrcTwitchOscBridge;

public partial class CashPaymentManagerWindow : Window
{
    public CashPaymentManagerWindow(CashPaymentManagerViewModel viewModel)
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        ThemeManager.ApplyToResources(Resources, ThemeManager.CurrentTheme);
        ThemeManager.ThemeChanged += OnThemeManagerThemeChanged;
        Closed += OnWindowClosed;
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed)
            return;
        try { DragMove(); }
        catch (InvalidOperationException) { }
    }

    private const string KoFiHostedRelayWarningTitle = "Ko-fi Relay Warning";

    private const string KoFiHostedRelayWarningMessage =
        "WARNING: To receive Ko-fi tips without any extra setup, Crystal Relay has to send them through the original developer's relay server.\n\n" +
        "While this is on, your Ko-fi verification token and the details of each tip (such as the supporter's name, amount, and message) pass through that server. " +
        "The server's code is private, so there is no way to check what is done with this information. It may be harmless, but you would be trusting the original developer with it.\n\n" +
        "Do you want to turn on the hosted Ko-fi relay anyway?";

    // The checkbox only reflects the setting; this handler decides, so the relay
    // can never be turned on without the warning being accepted.
    private void OnKoFiHostedRelayClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CashPaymentManagerViewModel viewModel)
            return;

        var cashPayments = viewModel.CashPayments;
        if (cashPayments.KoFiUseHostedRelay)
        {
            cashPayments.KoFiUseHostedRelay = false;
        }
        else if (ThemedDialogWindow.ShowYesNo(
                     this,
                     ThemeManager.CurrentTheme,
                     KoFiHostedRelayWarningTitle,
                     KoFiHostedRelayWarningMessage,
                     "Turn On",
                     "Cancel"))
        {
            cashPayments.KoFiHostedRelayWarningAccepted = true;
            cashPayments.KoFiUseHostedRelay = true;
        }

        if (sender is ToggleButton toggle)
            toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, cashPayments.KoFiUseHostedRelay);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnMinimizeClicked(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnThemeManagerThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => ThemeManager.ApplyToResources(Resources));
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeManagerThemeChanged;
        Closed -= OnWindowClosed;
        if (DataContext is IDisposable disposableDataContext)
            disposableDataContext.Dispose();
    }
}
