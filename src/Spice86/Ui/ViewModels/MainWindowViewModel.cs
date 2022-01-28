﻿namespace Spice86.UI.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;

using ReactiveUI;

using Serilog;

using Spice86.CLI;
using Spice86.Emulator;
using Spice86.Emulator.Devices.Video;
using Spice86.UI.EventArgs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// GUI of the emulator.<br/>
/// <ul>
/// <li>Displays the content of the video ram (when the emulator requests it)</li>
/// <li>Communicates keyboard and mouse events to the emulator</li>
/// </ul>
/// </summary>
public class MainWindowViewModel : ViewModelBase, IVideoKeyboardMouseIO, IDisposable {
    private static readonly ILogger _logger = Log.Logger.ForContext<MainWindowViewModel>();
    private readonly Configuration? _configuration;
    private readonly AutoResetEvent nextFrame = new AutoResetEvent(false);
    private bool _disposedValue;
    private Thread? _drawThread;
    private long _frameNumber = 0;
    private int _height = 1;
    private List<Key> _keysPressed = new();
    private Key? _lastKeyCode = null;
    private bool _leftButtonClicked;
    private int _mainCanvasScale = 4;
    private int _mouseX;
    private int _mouseY;
    private Action? _onKeyPressedEvent;
    private Action? _onKeyReleasedEvent;
    private ProgramExecutor? _programExecutor;
    private bool _rightButtonClicked;
    private AvaloniaList<VideoBufferViewModel> _videoBuffers = new();
    private int _width = 1;

    private FrameEventArgs? frame;

    public MainWindowViewModel() {
        if (Design.IsDesignMode) {
            return;
        }
        Configuration? configuration = GenerateConfiguration();
        _configuration = configuration;
        if (configuration == null) {
            Exit();
        }
        MainTitle = $"{nameof(Spice86)} {configuration?.Exe}";
        SetResolution(320, 200, 0);
        this.NextFrame += OnNextFrame;
        _drawThread = new Thread(DrawOnDedicatedThread);
        _drawThread.Start();
    }

    private event NextFrameEventHandler? NextFrame;

    public long FrameNumber {
        get { return _frameNumber; }
        set => this.RaiseAndSetIfChanged(ref _frameNumber, value);
    }

    public string? MainTitle { get; private set; }

    public AvaloniaList<VideoBufferViewModel> VideoBuffers {
        get => _videoBuffers;
        set => this.RaiseAndSetIfChanged(ref _videoBuffers, value);
    }

    public void AddBuffer(uint address, double scale, int bufferWidth, int bufferHeight, bool isPrimaryDisplay = false) {
        VideoBufferViewModel videoBuffer = new VideoBufferViewModel(this, bufferWidth, bufferHeight, scale, address, VideoBuffers.Count, isPrimaryDisplay);
        VideoBuffers.Add(videoBuffer);
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public void Draw(byte[] memory, Rgb[] palette) {
        FrameNumber++;
        this.NextFrame?.Invoke(new FrameEventArgs(memory, palette, FrameNumber, SortedBuffers()));
    }

    public void Exit() {
        if (Design.IsDesignMode) {
            return;
        }
        _programExecutor?.Dispose();
        Environment.Exit(0);
    }

    public int GetHeight() {
        return _height;
    }

    public Key? GetLastKeyCode() {
        return _lastKeyCode;
    }

    public int GetMouseX() {
        return _mouseX;
    }

    public int GetMouseY() {
        return _mouseY;
    }

    public IDictionary<uint, VideoBufferViewModel> GetVideoBuffers() {
        return VideoBuffers.ToDictionary(x => x.Address, x => x);
    }

    public int GetWidth() {
        return _width;
    }

    public bool IsKeyPressed(Key keyCode) {
        return _keysPressed.Contains(keyCode);
    }

    public bool IsLeftButtonClicked() {
        return _leftButtonClicked;
    }

    public bool IsRightButtonClicked() {
        return _rightButtonClicked;
    }

    public void OnKeyPressed(KeyEventArgs @event) {
        Key keyCode = @event.Key;
        if (!_keysPressed.Contains(keyCode)) {
            _logger.Information("Key pressed {@KeyPressed}", keyCode);
            _keysPressed.Add(keyCode);
            this._lastKeyCode = keyCode;
            RunOnKeyEvent(this._onKeyPressedEvent);
        }
    }

    public void OnKeyReleased(KeyEventArgs @event) {
        this._lastKeyCode = @event.Key;
        _logger.Information("Key released {@LastKeyCode}", _lastKeyCode);
        _keysPressed.Remove(_lastKeyCode.Value);
        RunOnKeyEvent(this._onKeyReleasedEvent);
    }

    /// <summary>
    /// async void in the only case where an exception won't be silenced and crash the process : an event handler.
    /// See: https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/master/AsyncGuidance.md
    /// </summary>
    public async void OnMainWindowOpened(object? sender, EventArgs e) {
        if (sender is Window) {
            await StartMachineAsync(_configuration);
        }
    }

    public void OnMouseClick(PointerEventArgs @event, bool click) {
        if (@event.Pointer.IsPrimary) {
            _leftButtonClicked = click;
        }

        if (@event.Pointer.IsPrimary == false) {
            _rightButtonClicked = click;
        }
    }

    public void OnMouseMoved(PointerEventArgs @event, Image image) {
        SetMouseX((int)@event.GetPosition(image).X);
        SetMouseY((int)@event.GetPosition(image).Y);
    }

    public void RemoveBuffer(uint address) {
        VideoBuffers.Remove(VideoBuffers.First(x => x.Address == address));
    }

    public void SetMouseX(int mouseX) {
        this._mouseX = mouseX;
    }

    public void SetMouseY(int mouseY) {
        this._mouseY = mouseY;
    }

    public void SetOnKeyPressedEvent(Action onKeyPressedEvent) {
        this._onKeyPressedEvent = onKeyPressedEvent;
    }

    public void SetOnKeyReleasedEvent(Action onKeyReleasedEvent) {
        this._onKeyReleasedEvent = onKeyReleasedEvent;
    }

    public void SetResolution(int width, int height, uint address) {
        VideoBuffers.Clear();
        this._width = width;
        this._height = height;
        AddBuffer(address, _mainCanvasScale, width, height, true);
    }

    protected virtual void Dispose(bool disposing) {
        if (!_disposedValue) {
            if (disposing) {
                foreach (VideoBufferViewModel buffer in VideoBuffers) {
                    buffer.Dispose();
                }
            }
            _disposedValue = true;
        }
    }

    private void DrawOnDedicatedThread() {
        while (true) {
            nextFrame.WaitOne(1000);
            if (frame == null) {
                continue;
            }
            foreach (VideoBufferViewModel videoBuffer in frame.SortedBuffers) {
                {
                    videoBuffer.Draw(frame.Memory, frame.Palette);
                }
            }
        }
    }

    private Configuration? GenerateConfiguration() {
        return new CommandLineParser().ParseCommandLine(Environment.GetCommandLineArgs());
    }

    private void OnNextFrame(FrameEventArgs e) {
        Interlocked.Exchange(ref this.frame, e);
        this.nextFrame.Set();
    }

    private void RunOnKeyEvent(Action? runnable) {
        if (runnable != null) {
            runnable.Invoke();
        }
    }

    private IEnumerable<VideoBufferViewModel> SortedBuffers() {
        return VideoBuffers.OrderBy(x => x.Address).Select(x => x);
    }

    private async Task StartMachineAsync(Configuration? configuration) {
        await Task.Factory.StartNew(() => {
            try {
                _programExecutor = new ProgramExecutor(this, configuration);
                _programExecutor.Run();
            } catch (Exception e) {
                _logger.Error(e, "An error occurred during execution");
            }
            Exit();
        });
    }
}