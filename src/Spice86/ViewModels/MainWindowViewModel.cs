﻿namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MessageBox.Avalonia.BaseWindows.Base;
using MessageBox.Avalonia.Enums;

using Serilog;

using Spice86;
using Spice86.Keyboard;
using Spice86.Views;
using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.Function.Dump;
using Spice86.Logging;
using Spice86.Shared;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

/// <summary>
/// GUI of the emulator.<br/>
/// <ul>
/// <li>Displays the content of the video ram (when the emulator requests it)</li>
/// <li>Communicates keyboard and mouse events to the emulator</li>
/// </ul>
/// </summary>
public partial class MainWindowViewModel : ObservableObject, IGui, IDisposable {
    private static readonly ILogger _logger = Serilogger.Logger.ForContext<MainWindowViewModel>();
    private Configuration? _configuration;
    private bool _disposedValue;
    private Thread? _emulatorThread;
    private bool _isSettingResolution = false;
    private PaletteWindow? _paletteWindow;
    private PerformanceWindow? _performanceWindow;
    private DebuggerWindow? _debuggerWindow;

    internal void OnKeyUp(KeyEventArgs e) => KeyUp?.Invoke(this, e);

    private ProgramExecutor? _programExecutor;

    [ObservableProperty]
    private AvaloniaList<VideoBufferViewModel> _videoBuffers = new();
    private ManualResetEvent _okayToContinueEvent = new(true);

    internal void OnKeyDown(KeyEventArgs e) => KeyDown?.Invoke(this, e);

    [ObservableProperty]
    private bool _isPaused = false;

    public event EventHandler<EventArgs>? KeyUp;
    public event EventHandler<EventArgs>? KeyDown;

    private bool _isMainWindowClosing = false;

    public MainWindowViewModel() {
        if (App.MainWindow is not null) {
            App.MainWindow.Closing += (s, e) => _isMainWindowClosing = true;
        }
    }

    [RelayCommand]
    public async Task DumpEmulatorStateToFile() {
        if (_programExecutor is null || _configuration is null) {
            return;
        }
        OpenFolderDialog ofd = new OpenFolderDialog() {
            Title = "Dump emulator state to directory...",
            Directory = _configuration.RecordedDataDirectory
        };
        if (Directory.Exists(_configuration.RecordedDataDirectory)) {
            ofd.Directory = _configuration.RecordedDataDirectory;
        }
        string? dir = _configuration.RecordedDataDirectory;
        if (App.MainWindow is not null) {
            dir = await ofd.ShowAsync(App.MainWindow);
        }
        if (string.IsNullOrWhiteSpace(dir)
        && !string.IsNullOrWhiteSpace(_configuration.RecordedDataDirectory)) {
            dir = _configuration.RecordedDataDirectory;
        }
        if (!string.IsNullOrWhiteSpace(dir)) {
            new RecorderDataWriter(dir, _programExecutor.Machine).DumpAll();
        }
    }

    [RelayCommand]
    private void Pause() {
        if (_emulatorThread is not null) {
            _okayToContinueEvent.Reset();
            IsPaused = true;
        }
    }

    [RelayCommand]
    private void Play() {
        if (_emulatorThread is not null) {
            _okayToContinueEvent.Set();
            IsPaused = false;
        }
    }

    public void SetConfiguration(string[] args) {
        Configuration? configuration = GenerateConfiguration(args);
        _configuration = configuration;
        if (configuration is null) {
            Exit();
            Environment.Exit(0);
        }
        SetMainTitle();
    }

    private void SetMainTitle() {
        MainTitle = $"{nameof(Spice86)} {_configuration?.Exe}";
    }

    [ObservableProperty]
    private string? _mainTitle;

    public void AddBuffer(uint address, double scale, int bufferWidth, int bufferHeight, bool isPrimaryDisplay = false) {
        VideoBufferViewModel videoBuffer = new VideoBufferViewModel(scale, bufferWidth, bufferHeight, address, VideoBuffers.Count, isPrimaryDisplay);
        Dispatcher.UIThread.Post(
            () => {
                if(!VideoBuffers.Any(x => x.Address == videoBuffer.Address)) {
                    VideoBuffers.Add(videoBuffer);
                }
            }
        );
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    [RelayCommand]
    public Task DebugExecutableCommand() {
        return StartNewExecutable(true);
    }

    [RelayCommand]
    public Task StartExecutable() {
        return StartNewExecutable();
    }

    private async Task StartNewExecutable(bool pauseOnStart = false) {
        if (App.MainWindow is not null) {
            OpenFileDialog? ofd = new OpenFileDialog() {
                Title = "Start Executable...",
                AllowMultiple = false,
                Filters = new(){
                    new FileDialogFilter() {
                        Extensions = {"exe", "com", "EXE", "COM" },
                        Name = "DOS Executables"
                    },
                    new FileDialogFilter() {
                        Extensions = { "*" },
                        Name = "All Files"
                    }
                }
            };
            string[]? files = await ofd.ShowAsync(App.MainWindow);
            if (files?.Any() == true && _configuration is not null) {
                _configuration.Exe = files[0];
                _configuration.ExeArgs = "";
                _configuration.CDrive = Path.GetDirectoryName(_configuration.Exe);
                Dispatcher.UIThread.Post(() => 
                    DisposeEmulator()
                , DispatcherPriority.MaxValue);
                SetMainTitle();
                _okayToContinueEvent = new(true);
                IsPaused = pauseOnStart;
                _programExecutor?.Machine.ExitEmulationLoop();
                while (_emulatorThread?.IsAlive == true) {
                    Dispatcher.UIThread.RunJobs();
                }
                RunEmulator();
            }
        }
    }

    private double _timeMultiplier = 1;

    public double TimeMultiplier {
        get => _timeMultiplier;
        set {
            this.SetProperty(ref _timeMultiplier, value);
            _programExecutor?.Machine.Timer.SetTimeMultiplier(_timeMultiplier);
        }
    }

    [RelayCommand]
    public void ShowDebugger() {
        if (_debuggerWindow != null) {
            _debuggerWindow.Activate();
        } else if (_programExecutor is not null) {
            _debuggerWindow = new DebuggerWindow() {
                DataContext = new DebuggerViewModel(
                    _programExecutor.Machine)
            };
            _debuggerWindow.Closed += (s, e) => _debuggerWindow = null;
            _debuggerWindow.Show();
        }
    }

    [RelayCommand]
    public void ShowPerformance() {
        if (_performanceWindow != null) {
            _performanceWindow.Activate();
        } else if (_programExecutor is not null) {
            _performanceWindow = new PerformanceWindow() {
                DataContext = new PerformanceViewModel(
                    _programExecutor.Machine, this)
            };
            _performanceWindow.Closed += (s, e) => _performanceWindow = null;
            _performanceWindow.Show();
        }
    }

    [RelayCommand]
    public void ShowColorPalette() {
        if (_paletteWindow != null) {
            _paletteWindow.Activate();
        } else {
            _paletteWindow = new PaletteWindow(this);
            _paletteWindow.Closed += (s, e) => _paletteWindow = null;
            _paletteWindow.Show();
        }
    }

    [RelayCommand]
    public void ResetTimeMultiplier() {
        if (_configuration is not null) {
            TimeMultiplier = _configuration.TimeMultiplier;
        }
    }

    private Rgb[] _palette = Array.Empty<Rgb>();

    public ReadOnlyCollection<Rgb> Palette => Array.AsReadOnly(_palette);

    public void Draw(byte[] memory, Rgb[] palette) {
        if (_disposedValue || _isSettingResolution) {
            return;
        }
        _palette = palette;
        foreach (VideoBufferViewModel videoBuffer in SortedBuffers()) {
            videoBuffer.Draw(memory, palette);
        }
    }

    public void Exit() {
        this.Dispose();
    }

    public int Height { get; private set; }

    public int MouseX { get; set; }

    public int MouseY { get; set; }

    public IDictionary<uint, IVideoBufferViewModel> VideoBuffersToDictionary =>
        VideoBuffers
        .ToDictionary(static x =>
            x.Address,
            x => (IVideoBufferViewModel)x);

    public int Width { get; private set; }

    public bool IsLeftButtonClicked { get; private set; }

    public bool IsRightButtonClicked { get; private set; }
    public void OnMainWindowOpened(object? sender, EventArgs e) => RunEmulator();

    private void RunEmulator() {
        if (_configuration is not null &&
            !string.IsNullOrWhiteSpace(_configuration.Exe) &&
            !string.IsNullOrWhiteSpace(_configuration.CDrive)) {
            RunMachine();
        }
    }

    public void OnMouseClick(PointerEventArgs @event, bool click) {
        if (@event.Pointer.IsPrimary) {
            IsLeftButtonClicked = click;
        }

        if (!@event.Pointer.IsPrimary) {
            IsRightButtonClicked = click;
        }
    }

    public void OnMouseMoved(PointerEventArgs @event, Image image) {
        MouseX = (int)@event.GetPosition(image).X;
        MouseY = (int)@event.GetPosition(image).Y;
    }

    public void RemoveBuffer(uint address) {
        VideoBuffers.Remove(VideoBuffers.First(x => x.Address == address));
    }

    public void SetResolution(int width, int height, uint address) {
        Dispatcher.UIThread.Post(() => {
            _isSettingResolution = true;
            DisposeBuffers();
            VideoBuffers = new();
            Width = width;
            Height = height;
            AddBuffer(address, 1, width, height, true);
            _isSettingResolution = false;
        }, DispatcherPriority.MaxValue);
    }

    private void DisposeBuffers() {
        for (int i = 0; i < VideoBuffers.Count; i++) {
            VideoBufferViewModel buffer = VideoBuffers[i];
            buffer.Dispose();
        }
        _videoBuffers.Clear();
    }

    protected virtual void Dispose(bool disposing) {
        if (!_disposedValue) {
            if (disposing) {
                PlayCommand.Execute(null);
                DisposeEmulator();
                _performanceWindow?.Close();
                _debuggerWindow?.Close();
                _paletteWindow?.Close();
                _okayToContinueEvent.Set();
                _okayToContinueEvent.Dispose();
                if (_emulatorThread?.IsAlive == true) {
                    _emulatorThread.Join();
                }
            }
            _disposedValue = true;
        }
    }

    private void DisposeEmulator() {
        DisposeBuffers();
        _programExecutor?.Dispose();
    }

    private static Configuration? GenerateConfiguration(string[] args) {
        return CommandLineParser.ParseCommandLine(args);
    }

    private IEnumerable<VideoBufferViewModel> SortedBuffers() {
        return VideoBuffers.OrderBy(static x => x.Address).Select(static x => x);
    }

    private async Task ShowEmulationErrorMessage(Exception e) {
        IMsBoxWindow<ButtonResult> errorMessage = MessageBox.Avalonia.MessageBoxManager
            .GetMessageBoxStandardWindow("An unhandled exception occured", e.GetBaseException().Message);
        if (!_disposedValue && !_isMainWindowClosing) {
            await errorMessage.ShowDialog(App.MainWindow);
        }
    }

    private void RunMachine() {
        _emulatorThread = new Thread(MachineThread) {
            Name = "Emulator"
        };
        _emulatorThread.Start();
    }

    // We use async void, but thankfully this doesn't generate an exception.
    // So this is OK...
    private async void OnEmulatorErrorOccured(Exception e) {
        await Dispatcher.UIThread.InvokeAsync(async () => await ShowEmulationErrorMessage(e));
    }

    private event Action<Exception>? EmulatorErrorOccured;

    private void MachineThread() {
        if (_configuration is null) {
            _logger.Error("No configuration available, cannot continue");
            throw new InvalidOperationException(
                $"{nameof(_configuration)} cannot be null when trying to run the emulator machine.");
        }
        try {
            _okayToContinueEvent.Set();
            _programExecutor = new ProgramExecutor(this, new AvaloniaKeyScanCodeConverter(), _configuration);
            TimeMultiplier = _configuration.TimeMultiplier;
            _programExecutor.Run();
        } catch (Exception e) {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _logger.Error(e, "An error occurred during execution");
            }
            EmulatorErrorOccured += OnEmulatorErrorOccured;
            EmulatorErrorOccured?.Invoke(e);
            EmulatorErrorOccured -= OnEmulatorErrorOccured;
        }
    }

    /// <inheritdoc />
    public void WaitOne() {
        _okayToContinueEvent.WaitOne(Timeout.Infinite);
    }
}