﻿namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Sound.PCSpeaker;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// PC speaker implementation.
/// </summary>
public sealed class PcSpeaker : DefaultIOPortHandler, IDisposable {
    private const int PcSpeakerPortNumber = 0x61;

    private bool _disposed;

    private readonly InternalSpeaker _pcSpeaker;

    /// <summary>
    /// Initializes a new instance of <see cref="PcSpeaker"/>
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="configuration">The emulator configuration.</param>
    public PcSpeaker(Machine machine, ILoggerService loggerService, Configuration configuration) : base(machine, configuration, loggerService) {
        _pcSpeaker = new(configuration);
    }

    /// <inheritdoc />
    public override byte ReadByte(int port) {
        byte value = _pcSpeaker.ReadByte(port);
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("PC Speaker get value {PCSpeakerValue}", ConvertUtils.ToHex8(value));
        }
        return value;
    }

    /// <inheritdoc />
    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(PcSpeakerPortNumber, this);
    }

    /// <inheritdoc />
    public override void WriteByte(int port, byte value) {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Verbose)) {
            _loggerService.Verbose("PC Speaker set value {PCSpeakerValue}", ConvertUtils.ToHex8(value));
        }

        _pcSpeaker.WriteByte(port, value);
    }

    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if(!_disposed){
            if(disposing) {
                _pcSpeaker.Dispose();
            }
            _disposed = true;
        }
    }
}