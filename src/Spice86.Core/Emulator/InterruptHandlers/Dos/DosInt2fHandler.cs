namespace Spice86.Core.Emulator.InterruptHandlers.Dos;

using Spice86.Shared.Interfaces;

using Serilog.Events;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Utils;

/// <summary>
/// Reimplementation of int2f
/// </summary>
public class DosInt2fHandler : InterruptHandler {
    private readonly ILoggerService _loggerService;

    public DosInt2fHandler(Machine machine, ILoggerService loggerService) : base(machine, loggerService) {
        _loggerService = loggerService;
        FillDispatchTable();
    }

    /// <inheritdoc />
    public override byte Index => 0x2f;

    /// <inheritdoc />
    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    private void FillDispatchTable() {
        _dispatchTable.Add(0x16, new Callback(0x16, () => ClearCFAndCX(true)));
        _dispatchTable.Add(0x15, new Callback(0x15, SendDeviceDriverRequest));
        _dispatchTable.Add(0x43, new Callback(0x43, () => ClearCFAndCX(true)));
        _dispatchTable.Add(0x46, new Callback(0x46, () => ClearCFAndCX(true)));
    }

    /// <summary>
    /// A service that does nothing, but set the carry flag to false, and CX to 0 to indicate success.
    /// <see href="https://github.com/FDOS/kernel/blob/master/kernel/int2f.asm"/> -> 'int2f_call:'.
    /// </summary>
    /// <param name="calledFromVm">Whether it was called by the emulator or not</param>
    public void ClearCFAndCX(bool calledFromVm) {
        SetCarryFlag(false, calledFromVm);
        _state.CX = 0;
    }

    public void SendDeviceDriverRequest() {
        ushort drive = _state.CX;
        uint deviceDriverRequestHeaderAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.BX);
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("SEND DEVICE DRIVER REQUEST Drive {Drive} Request header at: {Address:x8}",
                drive, deviceDriverRequestHeaderAddress);
        }

        // Carry flag signals error.
        _state.CarryFlag = true;
        // AX carries error reason.
        _state.AX = 0x000F; // Error code for "Invalid drive"
    }
}