// File:    DashboardViewModel.cs
// Project: SafeAuthenticator
// Author:  Gabriel Vergara Ezcurdia
// License: See README.md

using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeAuthenticator.Core;

namespace SafeAuthenticator.App.ViewModels;

/// <summary>
/// Dashboard: lists accounts with auto-refreshing OTP codes and progress rings.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase, IDisposable
{
    private readonly Func<DateTimeOffset> _timeProvider;
    private readonly DispatcherTimer? _timer;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    public ObservableCollection<OtpItemViewModel> Accounts { get; } = [];

    /// <summary>Creates a dashboard. The timer is only started when <paramref name="startTimer"/> is true.</summary>
    public DashboardViewModel(Func<DateTimeOffset>? timeProvider = null, bool startTimer = true)
    {
        _timeProvider = timeProvider ?? (() => DateTimeOffset.UtcNow);

        if (startTimer && Dispatcher.UIThread.CheckAccess())
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => Refresh();
            _timer.Start();
        }
    }

    /// <summary>Loads accounts into the dashboard and renders an initial code.</summary>
    public void Load(IEnumerable<OtpAccount> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        Accounts.Clear();
        foreach (var account in accounts)
        {
            Accounts.Add(new OtpItemViewModel(account));
        }

        Refresh();
    }

    /// <summary>Recomputes all codes for the current time.</summary>
    public void Refresh()
    {
        var now = _timeProvider();
        foreach (var item in Accounts)
        {
            item.Update(now);
        }
    }

    [RelayCommand]
    private void CopyCode(OtpItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        StatusMessage = $"Copied code for {item.Issuer}.";
    }

    public void Dispose()
    {
        _timer?.Stop();
    }
}
