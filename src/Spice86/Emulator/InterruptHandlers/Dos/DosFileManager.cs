﻿namespace Spice86.Emulator.InterruptHandlers.Dos;

using Serilog;

using Spice86.Emulator.Errors;
using Spice86.Emulator.Memory;
using Spice86.Utils;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// TODO : Fix it !
/// </summary>
public class DosFileManager {
    public const ushort FileHandleOffset = 5;
    private const int MaxOpenFiles = 15;
    private static readonly Dictionary<byte, string> FileOpenMode = new();
    private static readonly ILogger _logger = Log.Logger.ForContext<DosFileManager>();
    private string? currentDir;

    private string? currentMatchingFileSearchFolder;

    private string? currentMatchingFileSearchSpec;

    private ushort diskTransferAreaAddressOffset;

    private ushort diskTransferAreaAddressSegment;

    private Dictionary<char, string> driveMap = new();

    private IEnumerator<string>? matchingFilesIterator;

    private Memory memory;

    private OpenFile?[] openFiles = new OpenFile[MaxOpenFiles];

    static DosFileManager() {
        FileOpenMode.Add(0x00, "r");
        FileOpenMode.Add(0x01, "w");
        FileOpenMode.Add(0x02, "rw");
    }

    public DosFileManager(Memory memory) {
        this.memory = memory;
    }

    public DosFileOperationResult CloseFile(ushort fileHandle) {
        OpenFile? file = GetOpenFile(fileHandle);
        if (file == null) {
            return FileNotOpenedError(fileHandle);
        }

        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("Closed {@ClosedFileName}, file was loaded in ram in those addresses: {@ClosedFileAddresses}", file.GetName(), file.GetLoadMemoryRanges());
        }

        SetOpenFile(fileHandle, null);
        try {
            if (CountHandles(file) == 0) {
                // Only close the file if no other handle to it exist.
                file.GetRandomAccessFile().Close();
            }
        } catch (IOException e) {
            throw new UnrecoverableException("IOException while closing file", e);
        }

        return DosFileOperationResult.NoValue();
    }

    public DosFileOperationResult CreateFileUsingHandle(string fileName, ushort fileAttribute) {
        string? hostFileName = ToHostCaseSensitiveFileName(fileName, true);
        if (hostFileName == null) {
            return FileNotFoundErrorWithLog($"Could not find parent of {fileName} so cannot create file.");
        }

        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("Creating file {@HostFileName} with attribute {@FileAttribute}", hostFileName, fileAttribute);
        }
        var path = new FileInfo(hostFileName);
        try {
            if (File.Exists(path.FullName)) {
                File.Delete(path.FullName);
            }

            File.Create(path.FullName).Close();
        } catch (IOException e) {
            throw new UnrecoverableException("IOException while creating file", e);
        }

        return OpenFileInternal(fileName, hostFileName, "rw");
    }

    public DosFileOperationResult DuplicateFileHandle(ushort fileHandle) {
        OpenFile? file = GetOpenFile(fileHandle);
        if (file == null) {
            return FileNotOpenedError(fileHandle);
        }

        int? freeIndex = FindNextFreeFileIndex();
        if (freeIndex == null) {
            return NoFreeHandleError();
        }

        ushort dosIndex = (ushort)(freeIndex.Value + FileHandleOffset);
        SetOpenFile(dosIndex, file);
        return DosFileOperationResult.Value16(dosIndex);
    }

    /// <summary>
    /// </summary>
    /// <param name="fileSpec">a filename with ? when any character can match or * when multiple characters can match. Case is insensitive</param>
    /// <returns></returns>
    public DosFileOperationResult FindFirstMatchingFile(string fileSpec) {
        string hostSearchSpec = ToHostFileName(fileSpec);
        currentMatchingFileSearchFolder = hostSearchSpec.Substring(0, hostSearchSpec.LastIndexOf('/') + 1);
        if(string.IsNullOrWhiteSpace(currentMatchingFileSearchFolder) == false) {
            currentMatchingFileSearchSpec = hostSearchSpec.Replace(currentMatchingFileSearchFolder, "");
            Regex currentMatchingFileSearchSpecPattern = FileSpecToRegex(currentMatchingFileSearchSpec);
            try {
                string[] pathes = Directory.GetFiles(currentMatchingFileSearchFolder);
                List<string> matchingPathes = pathes.Where((p) => MatchesSpec(currentMatchingFileSearchSpecPattern, new FileInfo(p))).ToList();
                matchingFilesIterator = matchingPathes.GetEnumerator();
                return FindNextMatchingFile();
            } catch (IOException e) {
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                    _logger.Error(e, "Error while walking path {@CurrentMatchingFileSearchFolder} or getting attributes.", currentMatchingFileSearchFolder);
                }
            }
        }
        return DosFileOperationResult.Error(0x03);
    }

    public DosFileOperationResult FindNextMatchingFile() {
        if (matchingFilesIterator == null) {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _logger.Warning("No search was done");
            }
            return FileNotFoundError(null);
        }

        if (!matchingFilesIterator.MoveNext()) {
            return FileNotFoundErrorWithLog($"No more files matching {currentMatchingFileSearchSpec} in path {currentMatchingFileSearchFolder}");
        }

        var matching = matchingFilesIterator.MoveNext();
        if (matching) {
            try {
                UpdateDTAFromFile(matchingFilesIterator.Current);
            } catch (IOException e) {
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                    _logger.Warning(e, "Error while getting attributes.");
                }
                return FileNotFoundError(null);
            }
        }

        return DosFileOperationResult.NoValue();
    }

    public string GetDeviceName(ushort fileHandle) {
        return fileHandle switch {
            0 => "STDIN",
            1 => "STDOUT",
            2 => "STDERR",
            3 => "STDAUX",
            4 => "STDPRN",
            _ => throw new UnrecoverableException($"This is a programming error. getDeviceName called with fileHandle={fileHandle}")
        };
    }

    public ushort GetDiskTransferAreaAddressOffset() {
        return diskTransferAreaAddressOffset;
    }

    public ushort GetDiskTransferAreaAddressSegment() {
        return diskTransferAreaAddressSegment;
    }

    public DosFileOperationResult MoveFilePointerUsingHandle(byte originOfMove, ushort fileHandle, uint offset) {
        OpenFile? file = GetOpenFile(fileHandle);
        if (file == null) {
            return FileNotOpenedError(fileHandle);
        }

        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("Moving in file {@FileMove}", file.GetName());
        }
        FileStream randomAccessFile = file.GetRandomAccessFile();
        try {
            uint newOffset = Seek(randomAccessFile, originOfMove, offset);
            return DosFileOperationResult.Value32(newOffset);
        } catch (IOException e) {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _logger.Error(e, "An error occurred while seeking file {@Error}", e);
            }
            return DosFileOperationResult.Error(0x19);
        }
    }

    public DosFileOperationResult OpenFile(string fileName, byte rwAccessMode) {
        string? hostFileName = ToHostCaseSensitiveFileName(fileName, false);
        if (hostFileName == null) {
            return this.FileNotFoundError(fileName);
        }

        string openMode = FileOpenMode[rwAccessMode];
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("Opening file {@HostFileName} with mode {@OpenMode}", hostFileName, openMode);
        }

        return OpenFileInternal(fileName, hostFileName, openMode);
    }

    public DosFileOperationResult ReadFile(ushort fileHandle, ushort readLength, uint targetAddress) {
        OpenFile? file = GetOpenFile(fileHandle);
        if (file == null) {
            return FileNotOpenedError(fileHandle);
        }

        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("Reading from file {@FileName}", file.GetName());
        }

        byte[] buffer = new byte[readLength];
        int actualReadLength;
        try {
            actualReadLength = file.GetRandomAccessFile().Read(buffer, 0, readLength);
        } catch (IOException e) {
            throw new UnrecoverableException("IOException while reading file", e);
        }

        if (actualReadLength == -1) {
            // EOF
            return DosFileOperationResult.Value16(0);
        }

        if (actualReadLength > 0) {
            memory.LoadData(targetAddress, buffer, actualReadLength);
            file.AddMemoryRange(new MemoryRange(targetAddress, (uint)(targetAddress + actualReadLength - 1), file.GetName()));
        }

        return DosFileOperationResult.Value16((ushort)actualReadLength);
    }

    public DosFileOperationResult SetCurrentDir(string currentDir) {
        this.currentDir = ToHostCaseSensitiveFileName(currentDir, false);
        return DosFileOperationResult.NoValue();
    }

    public void SetDiskParameters(string currentDir, Dictionary<char, string> driveMap) {
        this.currentDir = currentDir;
        this.driveMap = driveMap;
    }

    public void SetDiskTransferAreaAddress(ushort diskTransferAreaAddressSegment, ushort diskTransferAreaAddressOffset) {
        this.diskTransferAreaAddressSegment = diskTransferAreaAddressSegment;
        this.diskTransferAreaAddressOffset = diskTransferAreaAddressOffset;
    }

    public DosFileOperationResult WriteFileUsingHandle(ushort fileHandle, ushort writeLength, uint bufferAddress) {
        if (IsWriteDeviceFileHandle(fileHandle)) {
            return WriteToDevice(fileHandle, writeLength, bufferAddress);
        }

        if (!IsValidFileHandle(fileHandle)) {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _logger.Warning("Invalid or unsupported file handle {@FileHandle}. Doing nothing.", fileHandle);
            }

            // Fake that we wrote, this could be used to write to stdout / stderr ...
            return DosFileOperationResult.Value16(writeLength);
        }

        OpenFile? file = GetOpenFile(fileHandle);
        if (file == null) {
            return FileNotOpenedError(fileHandle);
        }

        try {
            file.GetRandomAccessFile().Write(memory.GetRam(), (int)bufferAddress, writeLength);
        } catch (IOException e) {
            throw new UnrecoverableException("IOException while writing file", e);
        }

        return DosFileOperationResult.Value16(writeLength);
    }

    private int CountHandles(OpenFile openFileToCount) {
        int count = 0;
        foreach (OpenFile? openFile in openFiles) {
            if (openFile == openFileToCount) {
                count++;
            }
        }

        return count;
    }

    private int FileHandleToIndex(ushort fileHandle) {
        return fileHandle - FileHandleOffset;
    }

    private DosFileOperationResult FileNotFoundError(string? fileName) {
        return FileNotFoundErrorWithLog($"File {fileName} not found!");
    }

    private DosFileOperationResult FileNotFoundErrorWithLog(string message) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            _logger.Warning(message);
        }

        return DosFileOperationResult.Error(0x02);
    }

    private DosFileOperationResult FileNotOpenedError(int fileHandle) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            _logger.Warning("File not opened: {@FileHandle}", fileHandle);
        }
        return DosFileOperationResult.Error(0x06);
    }

    /// <summary>
    /// Converts a dos filespec to a regex pattern
    /// </summary>
    /// <param name="fileSpec"></param>
    /// <returns></returns>
    private Regex FileSpecToRegex(string fileSpec) {
        string regex = fileSpec.ToLowerInvariant();
        regex = regex.Replace(".", "[.]");
        regex = regex.Replace("?", ".");
        regex = regex.Replace("*", ".*");
        return new Regex(regex);
    }

    private int? FindNextFreeFileIndex() {
        for (int i = 0; i < openFiles.Length; i++) {
            if (openFiles[i] == null) {
                return i;
            }
        }

        return null;
    }

    private uint GetDiskTransferAreaAddressPhysical() {
        return MemoryUtils.ToPhysicalAddress(diskTransferAreaAddressSegment, diskTransferAreaAddressOffset);
    }

    private OpenFile? GetOpenFile(ushort fileHandle) {
        return openFiles[FileHandleToIndex(fileHandle)];
    }

    private bool IsValidFileHandle(ushort fileHandle) {
        return fileHandle >= FileHandleOffset && fileHandle <= MaxOpenFiles + FileHandleOffset;
    }

    private bool IsWriteDeviceFileHandle(ushort fileHandle) {
        return fileHandle > 0 && fileHandle < FileHandleOffset;
    }

    /// <summary>
    /// </summary>
    /// <param name="fileSpecPattern">a regex to match for a file, lower case</param>
    /// <param name="item">a path from which the file to match will be extracted</param>
    /// <returns>true if it matched, false otherwise</returns>
    private bool MatchesSpec(Regex fileSpecPattern, FileInfo item) {
        if ((item.Attributes & FileAttributes.Directory) == FileAttributes.Directory) {
            // Do not consider directories
            return false;
        }

        string fileName = item.Name.ToLowerInvariant();
        return fileSpecPattern.IsMatch(fileName);
    }

    private DosFileOperationResult NoFreeHandleError() {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            _logger.Warning("Could not find a free handle {@MethodName}", nameof(NoFreeHandleError));
        }
        return DosFileOperationResult.Error(0x04);
    }

    private DosFileOperationResult OpenFileInternal(string fileName, string? hostFileName, string openMode) {
        if (hostFileName == null) {
            // Not found
            return FileNotFoundError(fileName);
        }

        int? freeIndex = FindNextFreeFileIndex();
        if (freeIndex == null) {
            return NoFreeHandleError();
        }

        ushort dosIndex = (ushort)(freeIndex.Value + FileHandleOffset);
        try {
            FileStream? randomAccessFile = null;
            if (openMode == "r") {
                randomAccessFile = File.OpenRead(hostFileName);
            } else if (openMode == "w") {
                randomAccessFile = File.OpenWrite(hostFileName);
            } else  if (openMode == "rw") {
                randomAccessFile = File.Open(hostFileName, FileMode.Open);
            }
            if (randomAccessFile != null) {
                SetOpenFile(dosIndex, new OpenFile(fileName, dosIndex, randomAccessFile));
            }
        } catch (FileNotFoundException) {
            return FileNotFoundError(fileName);
        }

        return DosFileOperationResult.Value16(dosIndex);
    }

    private string ReplaceDriveWithHostPath(string fileName) {
        // Absolute path
        char driveLetter = fileName.ToUpper()[0];

        if (driveMap.TryGetValue(driveLetter, out var pathForDrive) == false) {
            throw new UnrecoverableException("Could not find a mapping for drive " + driveLetter);
        }

        return fileName.Replace(driveLetter + ":", pathForDrive);
    }

    private uint Seek(FileStream randomAccessFile, byte originOfMove, uint offset) {
        long newOffset;
        if (originOfMove == 0) {
            newOffset = offset; // seek from beginning, offset is good
        } else if (originOfMove == 1) {
            // seek from last read
            newOffset = randomAccessFile.Position + offset;
        } else {
            // seek from end
            newOffset = randomAccessFile.Length - offset;
        }

        randomAccessFile.Seek(newOffset, SeekOrigin.Begin);
        return (uint)newOffset;
    }

    private void SetOpenFile(ushort fileHandle, OpenFile? openFile) {
        openFiles[FileHandleToIndex(fileHandle)] = openFile;
    }

    private string? ToCaseSensitiveFileName(string? caseInsensitivePath) {
        if (string.IsNullOrWhiteSpace(caseInsensitivePath)) {
            return null;
        }

        string fileToProcess = ConvertUtils.toSlashPath(caseInsensitivePath);
        string? parentDir = Path.GetDirectoryName(fileToProcess);
        if (File.Exists(fileToProcess) || Directory.Exists(fileToProcess) || (parentDir != null && Directory.GetDirectories(parentDir).Length == 0)) {
            // file exists or root reached, no need to go further. Path found.
            return caseInsensitivePath;
        }

        string? parent = ToCaseSensitiveFileName(parentDir);
        if (parent == null) {
            // End of recursion, root reached
            return null;
        }
        
        // Now that parent is for sure on the disk, let's find the current file
        try {
            Regex fileToProcessRegex = FileSpecToRegex(Path.GetFileName(fileToProcess));
            string? filename = Directory
                .GetFiles(parent)
                .FirstOrDefault(x => fileToProcessRegex.IsMatch(x));
            return filename;
        } catch (IOException e) {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _logger.Warning(e, "Error while checking file {@CaseInsensitivePath}: {@Exception}", caseInsensitivePath, e);
            }
        }

        return null;
    }

    private ushort ToDosDate(DateTime localDate) {
        // https://stanislavs.org/helppc/file_attributes.html
        int day = localDate.Day;
        int month = localDate.Month;
        int dosYear = localDate.Year - 1980;
        return (ushort)((day & 0b11111) | ((month & 0b1111) << 5) | ((dosYear & 0b1111111) << 9));
    }

    private ushort ToDosTime(DateTime localTime) {
        // https://stanislavs.org/helppc/file_attributes.html
        int dosSeconds = localTime.Second / 2;
        int minutes = localTime.Minute;
        int hours = localTime.Hour;
        return (ushort)((dosSeconds & 0b11111) | ((minutes & 0b111111) << 5) | ((hours & 0b11111) << 11));
    }

    /// <summary>
    /// Converts dosFileName to a host file name.<br/>
    /// For this, need to:
    /// <ul>
    /// <li>Prefix either the current folder or the drive folder.</li>
    /// <li>Replace backslashes with slashes</li>
    /// <li>Find case sensitive matches for every path item (since DOS is case insensitive but some OS are not)</li>
    /// </ul>
    /// </summary>
    /// <param name="dosFileName"></param>
    /// <param name="forCreation">if true will try to find case sensitive match for only the parent of the file</param>
    /// <returns>the file name in the host file system, or null if nothing was found.</returns>
    public string? ToHostCaseSensitiveFileName(string dosFileName, bool forCreation) {
        string fileName = ToHostFileName(dosFileName);
        if (!forCreation) {
            return ToCaseSensitiveFileName(fileName);
        }
        string? parent = ToCaseSensitiveFileName(Path.GetDirectoryName(fileName));
        if (parent == null) {
            return null;
        }
        // Concat the folder to the requested file name
        return Path.Combine(parent, dosFileName);
    }

    /// <summary>
    /// Prefixes the given filename by either the mapped drive folder or the current folder depending on whether there is
    /// a Drive in the filename or not.<br/>
    /// Does not convert to case sensitive filename.
    /// </summary>
    /// <param name="dosFileName"></param>
    /// <returns></returns>
    private string ToHostFileName(string dosFileName) {
        string fileName = ConvertUtils.toSlashPath(dosFileName);
        if (fileName.Length >= 2 && fileName[1] == ':') {
            fileName = ReplaceDriveWithHostPath(fileName);
        } else if(string.IsNullOrWhiteSpace(currentDir) == false) {
            fileName = Path.Combine(currentDir, fileName);
        }

        return ConvertUtils.toSlashPath(fileName);
    }

    private void UpdateDTAFromFile(string matchingFile) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("Found matching file {@MatchingFile}", matchingFile);
        }
        DosDiskTransferArea dosDiskTransferArea = new(this.memory, this.GetDiskTransferAreaAddressPhysical());
        var attributes = new FileInfo(matchingFile);
        DateTime creationZonedDateTime = attributes.CreationTimeUtc;
        DateTime creationLocalDate = creationZonedDateTime.ToLocalTime();
        DateTime creationLocalTime = creationZonedDateTime.ToLocalTime();
        dosDiskTransferArea.SetFileDate(ToDosDate(creationLocalDate));
        dosDiskTransferArea.SetFileTime(ToDosTime(creationLocalTime));
        dosDiskTransferArea.SetFileSize((ushort)attributes.Length);
        dosDiskTransferArea.SetFileName(Path.GetFileName(matchingFile));
    }

    private DosFileOperationResult WriteToDevice(ushort fileHandle, ushort writeLength, uint bufferAddress) {
        string deviceName = GetDeviceName(fileHandle);
        byte[] buffer = memory.GetData(bufferAddress, writeLength);
        System.Console.WriteLine(deviceName + ConvertUtils.ToString(buffer));
        return DosFileOperationResult.Value16(writeLength);
    }
}