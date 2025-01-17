﻿namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer;
/// <summary>
/// Represents a DTA (Disk Transfer Area) in memory.
/// </summary>
public class DosDiskTransferArea : MemoryBasedDataStructureWithBaseAddress {
    /// <summary>
    /// The offset in bytes where the attribute field is located within the DTA.
    /// </summary>
    private const int AttributeOffset = 0x15;

    /// <summary>
    /// The offset in bytes where the file date field is located within the DTA.
    /// </summary>
    private const int FileDateOffset = 0x18;

    /// <summary>
    /// The offset in bytes where the file name field is located within the DTA.
    /// </summary>
    private const int FileNameOffset = 0x1E;

    /// <summary>
    /// The size in bytes of the file name field within the DTA.
    /// </summary>
    private const int FileNameSize = 13;

    /// <summary>
    /// The offset in bytes where the file size field is located within the DTA.
    /// </summary>
    private const int FileSizeOffset = 0x1A;

    /// <summary>
    /// The offset in bytes where the file time field is located within the DTA.
    /// </summary>
    private const int FileTimeOffset = 0x16;

    /// <summary>
    /// Initializes a new instance of the <see cref="DosDiskTransferArea"/> class.
    /// </summary>
    /// <param name="memory">The memory bus used for accessing the DTA.</param>
    /// <param name="baseAddress">The base address of the DTA within memory.</param>
    public DosDiskTransferArea(Memory memory, uint baseAddress) : base(memory, baseAddress) { }

    /// <summary>
    /// Gets or sets the attribute field of the DTA.
    /// </summary>
    public byte Attribute { get => GetUint8(AttributeOffset); set => SetUint8(AttributeOffset, value); }

    /// <summary>
    /// Gets or sets the file date field of the DTA.
    /// </summary>
    public ushort FileDate { get => GetUint16(FileDateOffset); set => SetUint16(FileDateOffset, value); }

    /// <summary>
    /// Gets or sets the file name field of the DTA.
    /// </summary>
    public string FileName {
        get => GetZeroTerminatedString(FileNameOffset, FileNameSize);
        set => SetZeroTerminatedString(FileNameOffset, value, FileNameSize);
    }

    /// <summary>
    /// Gets or sets the file size field of the DTA.
    /// </summary>
    public ushort FileSize { get => GetUint16(FileSizeOffset); set => SetUint16(FileSizeOffset, value); }

    /// <summary>
    /// Gets or sets the file time field of the DTA.
    /// </summary>
    public ushort FileTime { get => GetUint16(FileTimeOffset); set => SetUint16(FileTimeOffset, value); }
}
