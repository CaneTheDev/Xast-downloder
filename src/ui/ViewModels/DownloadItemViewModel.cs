using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XastDownloader.Core.Models;

namespace ui.ViewModels;

public partial class DownloadItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _fileName = "";

    [ObservableProperty]
    private string _url = "";

    [ObservableProperty]
    private double _progress = 0;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _speed = "";

    [ObservableProperty]
    private string _downloaded = "";

    [ObservableProperty]
    private string _eta = "";

    [ObservableProperty]
    private int _connections = 0;

    [ObservableProperty]
    private int _requestedConnections = 0;

    [ObservableProperty]
    private bool _serverSupportsRanges = true;

    [ObservableProperty]
    private DownloadStatus _downloadStatus;

    public string ConnectionsDisplay => ServerSupportsRanges 
        ? $"{Connections} / {RequestedConnections}" 
        : $"1 (server doesn't support ranges)";

    public string TaskId { get; set; } = "";

    public Action<DownloadItemViewModel>? OnPause { get; set; }
    public Action<DownloadItemViewModel>? OnResume { get; set; }
    public Action<DownloadItemViewModel>? OnCancel { get; set; }

    public bool IsActive => DownloadStatus == DownloadStatus.Downloading;
    public bool CanPause => DownloadStatus == DownloadStatus.Downloading;
    public bool CanResume => DownloadStatus == DownloadStatus.Paused;
    public bool CanCancel => DownloadStatus == DownloadStatus.Downloading || DownloadStatus == DownloadStatus.Paused;

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        OnPause?.Invoke(this);
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume()
    {
        OnResume?.Invoke(this);
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        OnCancel?.Invoke(this);
    }

    partial void OnDownloadStatusChanged(DownloadStatus value)
    {
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanCancel));
    }

    partial void OnConnectionsChanged(int value)
    {
        OnPropertyChanged(nameof(ConnectionsDisplay));
    }

    partial void OnRequestedConnectionsChanged(int value)
    {
        OnPropertyChanged(nameof(ConnectionsDisplay));
    }

    partial void OnServerSupportsRangesChanged(bool value)
    {
        OnPropertyChanged(nameof(ConnectionsDisplay));
    }
}
