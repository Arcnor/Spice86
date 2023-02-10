﻿using Spice86.Core.DI;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Serilog;

using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.VM;
using Spice86.Logging;

/// <summary>
/// Reimplementation of int20
/// </summary>
public class DosInt20Handler : InterruptHandler {
    private readonly ILoggerService _loggerService;

    public DosInt20Handler(Machine machine, ILoggerService loggerService) : base(machine) {
        _loggerService = loggerService;
    }

    public override byte Index => 0x20;

    public override void Run() {
        _loggerService.Information("PROGRAM TERMINATE");
        _cpu.IsRunning = false;
    }
}