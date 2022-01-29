namespace Spice86.Emulator;

using CommandLine;

using Spice86.Emulator.Function;

using System;

/// <summary> Configuration for spice86, that is what to run and how. Set on startup. </summary>
public class Configuration {

    [Option('c', nameof(CDrive), Required = false, HelpText = "path to C drive, default is .")]
    public string? CDrive { get; set; } = Environment.CurrentDirectory;

    [Option('d', nameof(DefaultDumpDirectory), Required = false, HelpText = "Directory to dump data to when not specified otherwise. Workin directory if blank")]
    public string? DefaultDumpDirectory { get; set; } = Environment.CurrentDirectory;


    [Option('e', nameof(Exe), Default = null, Required = true, HelpText = "Path to executable")]
    public string? Exe { get; set; }

    [Option('a', nameof(ExeArgs), Default = null, Required = false, HelpText = "List of parameters to give to the emulated program")]
    public string? ExeArgs { get; set; }


    [Option('x', nameof(ExpectedChecksum), Default = null, Required = false, HelpText = "Hexadecimal string representing the expected checksum of the checksum")]
    public string? ExpectedChecksum { get; set; }
    public byte[] ExpectedChecksumValue {get; set;} = Array.Empty<byte>();


    [Option('f', nameof(FailOnUnhandledPort), Default = false, Required = false, HelpText = "If true, will fail when encountering an unhandled IO port. Useful to check for unimplemented hardware. false by default.")]
    public bool FailOnUnhandledPort { get; set; }

    [Option('g', nameof(GdbPort), Default = null, Required = false, HelpText = "gdb port, if empty gdb server will not be created. If not empty, application will pause until gdb connects")]
    public int? GdbPort { get; set; }

    public bool InstallInterruptVector { get; set; } = true;

    [Option('o', nameof(OverrideSupplierClass), Default = null, Required = false, HelpText = "Name of a class in the current folder that will generate the initial function informations. See documentation for more information.")]
    public string? OverrideSupplierClass { get; set; }

    /// <summary>
    /// Instantiated <see cref="OverrideSupplierClass"/>. Created by <see cref="CLI.CommandLineParser"/>
    /// </summary>
    public IOverrideSupplier? OverrideSupplier { get; set; }

    [Option('p', nameof(ProgramEntryPointSegment), Default = 0x01ED, Required = false, HelpText = "Segment where to load the program. DOS PSP and MCB will be created before it.")]
    public int ProgramEntryPointSegment { get; set; }

    [Option('u', nameof(UseCodeOverride), Default = false, Required = false, HelpText = "<true or false> if false it will use the names provided by overrideSupplierClassName but not the code")]
    public bool UseCodeOverride { get; set; }

    /// <summary>
    /// Only for <see cref="Devices.Timer.Timer"/>
    /// </summary>
    [Option('i', nameof(InstructionsPerSecond), Required = false, HelpText = "<number of instructions that have to be executed executed by the emulator to consider a second passed> if blank will use time based timer.")]
    public long? InstructionsPerSecond { get; set; }

    [Option('t', nameof(TimeMultiplier), Default =1, Required = false, HelpText = "<time multiplier> if >1 will go faster, if <1 will go slower.")]
    public double TimeMultiplier { get; set; }
}