﻿using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaAppMVVM.Data;
using AvaloniaAppMVVM.ViewModels;
using LibGit2Sharp;
using LoadingIndicators.Avalonia;
using Newtonsoft.Json.Linq;
using SocketServer;

namespace AvaloniaAppMVVM.Views;

public class ProcessesTemplate
{
    public ProcessesTemplate(IProcess process)
    {
        Process = process;
    }

    public string? Id => Process.Id;
    public IProcess Process { get; }
    public Expander Expander { get; set; }

    // controls
    public LoadingIndicator BusyIndicator { get; set; }
    public LoadingIndicator QueuedIndicator { get; set; }
    public PathIcon SuccessIcon { get; set; }
    public PathIcon FailedIcon { get; set; }
    public TextBox LogText { get; set; }
    public TextBlock TimeText { get; set; }

    public bool IsBusy
    {
        get => Process.IsBusy;
        set
        {
            Process.IsBusy = value;
            BusyIndicator.IsVisible = value;
        }
    }

    public bool IsQueued
    {
        get => Process.IsQueued;
        set
        {
            Process.IsQueued = value;
            QueuedIndicator.IsVisible = value;
        }
    }

    public bool Succeeded
    {
        get => Process.Succeeded;
        set
        {
            Process.Succeeded = value;
            SuccessIcon.IsVisible = value;
        }
    }

    public bool Failed
    {
        get => Process.Failed;
        set
        {
            Process.Failed = value;
            FailedIcon.IsVisible = value;
        }
    }

    public string? Logs
    {
        get => Process.Logs;
        set
        {
            Process.Logs = value;
            LogText.Text = value;
        }
    }

    public string? Time
    {
        get => Process.TotalTime;
        set
        {
            Process.TotalTime = value;
            TimeText.Text = value;
        }
    }
}

public class Icons
{
    public static Geometry Checkmark =>
        Application.Current!.TryGetResource("checkmark_regular", out var res)
            ? (Geometry)res
            : null;

    public static Geometry Error =>
        Application.Current!.TryGetResource("error_circle_regular", out var res)
            ? (Geometry)res
            : null;
}

public partial class HomePageView : MyUserControl<HomePageViewModel>
{
    private readonly List<ProcessesTemplate> _processes =
    [
        new ProcessesTemplate(new CiProcess { Id = "PreBuild" }),
        new ProcessesTemplate(new CiProcess { Id = "Build" }),
        new ProcessesTemplate(new CiProcess { Id = "Deploy" }),
        new ProcessesTemplate(new CiProcess { Id = "Hooks" })
    ];

    public HomePageView()
    {
        InitializeComponent();
        BuildView();
    }

    protected override void OnInit()
    {
        _viewModel.Project = _project;

        ConnectAsync();
        SetGitBranchComboBoxItems();
    }

    protected override void OnPreSave() { }

    private async void ConnectAsync()
    {
        ServerStatus.Text = "Connecting...";
        while (!App.BuildClient.IsConnected)
            await Task.Delay(10);
        ServerStatus.Text = "Connected";
    }

    #region Build View

    private void BuildView()
    {
        for (var i = 0; i < _processes.Count; i++)
        {
            var process = _processes[i];
            process.LogText = new TextBox
            {
                Name = $"{process.Id}_Log",
                Text = string.Empty,
                IsReadOnly = true,
                MaxHeight = 500,
            };
            var expander = new Expander
            {
                Name = $"{process.Id}_Expander",
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 0, 0),
                CornerRadius = new CornerRadius(15),
                Header = BuildGridHeader(process),
                Content = process.LogText
            };

            ProcessContainer.Children.Add(expander);
            process.Expander = expander;
        }
    }

    private static Grid BuildGridHeader(ProcessesTemplate process)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto *") };

        // Icons
        // busy
        process.BusyIndicator = new LoadingIndicator
        {
            Name = $"{process.Id}_BusyIndicator",
            Mode = LoadingIndicatorMode.Arc,
            SpeedRatio = 1.2,
            IsVisible = false
        };
        grid.Children.Add(process.BusyIndicator);

        // queued
        process.QueuedIndicator = new LoadingIndicator
        {
            Name = $"{process.Id}_QueuedIndicator",
            Mode = LoadingIndicatorMode.ThreeDots,
            SpeedRatio = 0,
            IsVisible = false
        };
        grid.Children.Add(process.QueuedIndicator);

        // success
        process.SuccessIcon = new PathIcon
        {
            Name = $"{process.Id}_SuccessIcon",
            Foreground = Brushes.Green,
            Data = Icons.Checkmark,
            IsVisible = false
        };
        grid.Children.Add(process.SuccessIcon);

        // failed
        process.FailedIcon = new PathIcon
        {
            Name = $"{process.Id}_FailedIcon",
            Foreground = Brushes.Firebrick,
            Data = Icons.Error,
            IsVisible = false
        };
        grid.Children.Add(process.FailedIcon);

        // Label text
        var textStack = new StackPanel { Orientation = Orientation.Horizontal };
        textStack.Children.Add(
            new TextBlock
            {
                Name = $"{process.Id}_Label",
                Text = process.Process.Id,
                Margin = new Thickness(30, 0, 0, 0),
                FontSize = 20,
                FontWeight = FontWeight.Black,
                VerticalAlignment = VerticalAlignment.Center
            }
        );
        // time text
        process.TimeText = new TextBlock
        {
            Name = $"{process.Id}_Time",
            Text = process.Process.TotalTime,
            Margin = new Thickness(30, 0, 0, 0),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        textStack.Children.Add(process.TimeText);

        grid.Children.Add(textStack);
        Grid.SetColumn(textStack, 1);

        return grid;
    }

    #endregion

    #region Build Process

    private void Button_StartBuild_OnClick(object? sender, RoutedEventArgs e)
    {
        RefreshProcesses();

        var project = new JObject
        {
            ["ProjectGuid"] = _project.Guid,
            // TODO: get these from a form in home screen rather than project settings
            ["BuildTargets"] = JArray.FromObject(_project.BuildTargets.Select(x => x.Name)),
            ["Branch"] = BranchComboBox.SelectedItem?.ToString(),
            ["GitUrl"] = _project.Settings.GitRepositoryUrl
        };
        App.BuildClient.SendJson(project);
    }

    private void RefreshProcesses()
    {
        // refresh processes
        foreach (var process in _processes)
        {
            process.IsQueued = false;
            process.Failed = false;
            process.Succeeded = false;
            process.IsBusy = false;
            process.Logs = string.Empty;
        }
    }

    private void SetGitBranchComboBoxItems()
    {
        if (string.IsNullOrEmpty(_project.Location))
            return;

        using var repo = new Repository(_project.Location);
        var branches = repo.Branches.Where(x => x.IsRemote && !x.FriendlyName.Contains("HEAD"))
            .Select(x => x.FriendlyName.Replace(x.RemoteName, string.Empty).Trim('/'))
            .ToList();

        BranchComboBox.ItemsSource = branches;
        BranchComboBox.SelectedIndex = 0;
    }

    #endregion
}
