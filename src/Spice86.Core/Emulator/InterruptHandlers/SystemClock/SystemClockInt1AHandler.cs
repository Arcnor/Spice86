﻿using Spice86.Logging;

namespace Spice86.Core.Emulator.InterruptHandlers.SystemClock;

using Serilog;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Timer;
using Spice86.Core.Emulator.VM;

/// <summary>
/// Implementation of int1A.
/// </summary>
public class SystemClockInt1AHandler : InterruptHandler {
    private readonly ILoggerService _loggerService;
    private readonly TimerInt8Handler _timerHandler;

    public SystemClockInt1AHandler(Machine machine, ILoggerService loggerService, TimerInt8Handler timerHandler) : base(machine) {
        _loggerService = loggerService;
        _timerHandler = timerHandler;
        _dispatchTable.Add(0x00, new Callback(0x00, SetSystemClockCounter));
        _dispatchTable.Add(0x01, new Callback(0x01, GetSystemClockCounter));
        _dispatchTable.Add(0x81, new Callback(0x81, TandySoundSystemUnhandled));
        _dispatchTable.Add(0x82, new Callback(0x82, TandySoundSystemUnhandled));
        _dispatchTable.Add(0x83, new Callback(0x83, TandySoundSystemUnhandled));
        _dispatchTable.Add(0x84, new Callback(0x84, TandySoundSystemUnhandled));
        _dispatchTable.Add(0x85, new Callback(0x85, TandySoundSystemUnhandled));
    }

    public override byte Index => 0x1A;

    public void GetSystemClockCounter() {
        uint value = _timerHandler.TickCounterValue;
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("GET SYSTEM CLOCK COUNTER {@SystemClockCounterValue}", value);
        }

        // let's say it never overflows
        _state.AL = 0;
        _state.CX = (ushort)(value >> 16);
        _state.DX = (ushort)value;
    }

    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    public void SetSystemClockCounter() {
        uint value = (ushort)(_state.CX << 16 | _state.DX);
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("SET SYSTEM CLOCK COUNTER {@SystemClockCounterValue}", value);
        }
        _timerHandler.TickCounterValue = value;
    }

    private void TandySoundSystemUnhandled() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _loggerService.Information("TANDY SOUND SYSTEM IS NOT IMPLEMENTED");
        }
    }
}