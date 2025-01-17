﻿using Spice86.Logging;

namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Serilog;

using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

/// <summary>
/// Crude implementation of Int9
/// </summary>
public class BiosKeyboardInt9Handler : InterruptHandler {
    private readonly ILoggerService _loggerService;

    private readonly Keyboard _keyboard;

    public BiosKeyboardInt9Handler(Machine machine, ILoggerService loggerService) : base(machine, loggerService) {
        _loggerService = loggerService;
        _keyboard = machine.Keyboard;
        BiosKeyboardBuffer = new BiosKeyboardBuffer(machine.Memory);
        BiosKeyboardBuffer.Init();
    }

    public BiosKeyboardBuffer BiosKeyboardBuffer { get; }

    /// <inheritdoc />
    public override byte Index => 0x9;

    /// <inheritdoc />
    public override void Run() {
        byte? scancode = _keyboard.LastKeyboardInput.ScanCode;
        if (scancode is null) {
            return;
        }

        byte ascii = _keyboard.LastKeyboardInput.AsciiCode ?? 0;

        if(_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("{BiosInt9KeyReceived}", ascii);
        }

        BiosKeyboardBuffer.AddKeyCode((ushort)(scancode.Value << 8 | ascii));
    }
}