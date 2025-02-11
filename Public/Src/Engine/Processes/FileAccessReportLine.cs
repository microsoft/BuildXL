// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using BuildXL.Utilities.Core;
using BuildXL.Native.IO;
using static BuildXL.Utilities.Core.FormattableStringEx;

#nullable enable

namespace BuildXL.Processes
{
    /// <summary>
    /// Low-level parser for file access report lines
    /// </summary>
    internal static class FileAccessReportLine
    {
        /// <summary>
        /// A string-like type that wraps <see cref="ReadOnlyMemory{T}"/> and acts like a string for no allocation purposes.
        /// </summary>
        private readonly record struct MemoryString(ReadOnlyMemory<char> Data)
        {
            /// <inheritdoc />
            public override int GetHashCode()
            {
#if NET5_0_OR_GREATER
                var hashCode = new HashCode();
                var span = Data.Span;
                hashCode.AddBytes(MemoryMarshal.Cast<char, byte>(span));
                return hashCode.ToHashCode();
#else
                // Not a very efficient version for full framework
                return Data.ToString().GetHashCode();
#endif
            }

            /// <inheritdoc />
            public bool Equals(MemoryString other)
            {
                return Data.Span.SequenceEqual(other.Data.Span);
            }
        }

        /// <summary>
        /// Mapping of "operation name" to <see cref="ReportedFileOperation"/>
        /// </summary>
        private static readonly Dictionary<string, ReportedFileOperation> s_operations =
            new Dictionary<string, ReportedFileOperation>(StringComparer.Ordinal)
            {
                { "CreateFile", ReportedFileOperation.CreateFile },
                { "CreateDirectory", ReportedFileOperation.CreateDirectory },
                { "RemoveDirectory", ReportedFileOperation.RemoveDirectory },
                { "RemoveDirectory_Source", ReportedFileOperation.RemoveDirectorySource },
                { "GetFileAttributes", ReportedFileOperation.GetFileAttributes },
                { "GetFileAttributesEx", ReportedFileOperation.GetFileAttributesEx },
                { "FindFirstFileEx", ReportedFileOperation.FindFirstFileEx },
                { "FindNextFile", ReportedFileOperation.FindNextFile },
                { "CopyFile_Source", ReportedFileOperation.CopyFileSource },
                { "CopyFile_Dest", ReportedFileOperation.CopyFileDestination },
                { "CreateHardLink_Source", ReportedFileOperation.CreateHardLinkSource },
                { "CreateHardLink_Dest", ReportedFileOperation.CreateHardLinkDestination },
                { "MoveFile_Source", ReportedFileOperation.MoveFileSource },
                { "MoveFile_Dest", ReportedFileOperation.MoveFileDestination },
                { "ZwSetRenameInformationFile_Source", ReportedFileOperation.ZwSetRenameInformationFileSource },
                { "ZwSetRenameInformationFile_Dest", ReportedFileOperation.ZwSetRenameInformationFileDest },
                { "ZwSetLinkInformationFile", ReportedFileOperation.ZwSetLinkInformationFile },
                { "ZwSetDispositionInformationFile", ReportedFileOperation.ZwSetDispositionInformationFile },
                { "ZwSetModeInformationFile", ReportedFileOperation.ZwSetModeInformationFile },
                { "ZwSetFileNameInformationFile_Source", ReportedFileOperation.ZwSetFileNameInformationFileSource },
                { "ZwSetFileNameInformationFile_Dest", ReportedFileOperation.ZwSetFileNameInformationFileDest },
                { "SetFileInformationByHandle_Source", ReportedFileOperation.SetFileInformationByHandleSource },
                { "SetFileInformationByHandle_Dest", ReportedFileOperation.SetFileInformationByHandleDest },
                { "DeleteFile", ReportedFileOperation.DeleteFile },
                { "Process", ReportedFileOperation.Process },
                { "ProcessExit", ReportedFileOperation.ProcessExit },
                { "NtQueryDirectoryFile", ReportedFileOperation.NtQueryDirectoryFile },
                { "ZwQueryDirectoryFile", ReportedFileOperation.ZwQueryDirectoryFile },
                { "NtCreateFile", ReportedFileOperation.NtCreateFile },
                { "ZwCreateFile", ReportedFileOperation.ZwCreateFile },
                { "ZwOpenFile", ReportedFileOperation.ZwOpenFile },
                { "CreateSymbolicLink_Source", ReportedFileOperation.CreateSymbolicLinkSource },
                { "ReparsePointTarget", ReportedFileOperation.ReparsePointTarget },
                { "ReparsePointTargetCached", ReportedFileOperation.ReparsePointTargetCached },
                { "ChangedReadWriteToReadAccess", ReportedFileOperation.ChangedReadWriteToReadAccess },
                { "FirstAllowWriteCheckInProcess", ReportedFileOperation.FirstAllowWriteCheckInProcess },
                { "ProcessRequiresPtrace", ReportedFileOperation.ProcessRequiresPTrace },
                { "ProcessBreakaway", ReportedFileOperation.ProcessBreakaway },
                { "MoveFileWithProgress_Source", ReportedFileOperation.MoveFileWithProgressSource },
                { "MoveFileWithProgress_Dest", ReportedFileOperation.MoveFileWithProgressDest },
                { "MultipleOperations", ReportedFileOperation.MultipleOperations },
                { "CreateProcess", ReportedFileOperation.CreateProcess },
                { "ProcessTreeCompletedAck", ReportedFileOperation.ProcessTreeCompletedAck },
                { FileOperationLinux.Operations.ProcessExec.ToString(), ReportedFileOperation.ProcessExec },
                { FileOperationLinux.Operations.Readlink.ToString(), ReportedFileOperation.Readlink },
                { FileOperationLinux.Operations.ReadFile.ToString(), ReportedFileOperation.ReadFile },
                { FileOperationLinux.Operations.WriteFile.ToString(), ReportedFileOperation.WriteFile },
                { FileOperationLinux.Operations.CreateHardlinkSource.ToString(), ReportedFileOperation.CreateHardlinkSource },
                { FileOperationLinux.Operations.CreateHardlinkDest.ToString(), ReportedFileOperation.CreateHardlinkDest },
                { FileOperationLinux.Operations.OpenDirectory.ToString(), ReportedFileOperation.OpenDirectory },
                { FileOperationLinux.Operations.Close.ToString(), ReportedFileOperation.Close },
                { FileOperationLinux.Operations.Probe.ToString(), ReportedFileOperation.Probe }
            };

        private static readonly Dictionary<MemoryString, ReportedFileOperation> s_memoryStringBasedOperations = 
            s_operations.ToDictionary(kvp => new MemoryString(kvp.Key.AsMemory()), kvp => kvp.Value);

        /// <summary>
        /// Tries obtaining <see cref="ReportedFileOperation"/> for a given <paramref name="operationName"/>.
        /// </summary>
        public static bool TryGetOperation(string operationName, out ReportedFileOperation operation)
        {
            return s_operations.TryGetValue(operationName, out operation);
        }
        
        /// <summary>
        /// Tries obtaining <see cref="ReportedFileOperation"/> for a given <paramref name="operationName"/>.
        /// </summary>
        public static bool TryGetOperation(ReadOnlyMemory<char> operationName, out ReportedFileOperation operation)
        {
            return s_memoryStringBasedOperations.TryGetValue(new MemoryString(operationName), out operation);
        }

        /// <summary>
        /// Parses a string representing a file access
        /// </summary>
        /// <remarks>
        /// The parent process id is always set to zero for Windows. For Linux we send out the ppid for making up for some potential missing process start events
        /// </remarks>
        public static bool TryParse(
            ref string line,
            out uint processId,
            out uint parentProcessId,
            out uint id,
            out uint correlationId,
            out ReportedFileOperation operation,
            out RequestedAccess requestedAccess,
            out FileAccessStatus status,
            out bool explicitlyReported,
            out uint error,
            out uint rawError,
            out Usn usn,
            out DesiredAccess desiredAccess,
            out ShareMode shareMode,
            out CreationDisposition creationDisposition,
            out FlagsAndAttributes flagsAndAttributes,
            out FlagsAndAttributes openedFileOrDirectoryAttributes,
            out AbsolutePath absolutePath,
            out string? path,
            out string? enumeratePattern,
            out string? processArgs,
            out string? errorMessage)
        {
            // TODO: Task 138817: Refactor passing and parsing of report data from native to managed code

            operation = ReportedFileOperation.Unknown;
            requestedAccess = RequestedAccess.None;
            status = FileAccessStatus.None;
            processId = parentProcessId = error = rawError = 0;
            id = correlationId = 0;
            usn = default;
            explicitlyReported = false;
            desiredAccess = 0;
            shareMode = ShareMode.FILE_SHARE_NONE;
            creationDisposition = 0;
            flagsAndAttributes = 0;
            openedFileOrDirectoryAttributes = 0;
            absolutePath = AbsolutePath.Invalid;
            path = null;
            enumeratePattern = null;
            processArgs = null;
            errorMessage = string.Empty;

            const int MinItemsCount = 16;
#if NET5_0_OR_GREATER
            var i = line.IndexOf(':', StringComparison.Ordinal);
#else
            var i = line.IndexOf(':');
#endif
            var index = 0;

            if (i == -1)
            {
                errorMessage = $"Unexpected message format. Can't find ':' in message '{line}'.";
                return false;
            }

            SpanSplitEnumerator<char> items = line.AsSpan(i + 1).Split('|');

            if (!TryGetOperation(line.AsMemory(start: 0, length: i), out operation))
            {
                // We could consider the report line malformed in this case; but in practice it is easy to forget to update this parser
                // after adding a new call. So let's be conservative about throwing the line out so long as we can parse the important bits to follow.
                operation = ReportedFileOperation.Unknown;
            }

            if (
                tryGetNextItem(ref items, out var span) && uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out processId) &&
                tryGetNextItem(ref items, out span) && uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id) &&
                tryGetNextItem(ref items, out span) && uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out correlationId) &&
                tryGetNextItem(ref items, out span) && uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var requestedAccessValue) &&
                tryGetNextItem(ref items, out span) && uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var statusValue) &&
                tryGetNextItem(ref items, out span) && uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var explicitlyReportedValue) &&
                tryGetNextItem(ref items, out span) && uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out error) &&
                tryGetNextItem(ref items, out span) && uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rawError) &&
                tryGetNextItem(ref items, out span) && ulong.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var usnValue) &&
                tryGetNextItem(ref items, out span) && uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var desiredAccessValue) &&
                tryGetNextItem(ref items, out span) && uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var shareModeValue) &&
                tryGetNextItem(ref items, out span) && uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var creationDispositionValue) &&
                tryGetNextItem(ref items, out span) && uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var flagsAndAttributesValue) &&
                tryGetNextItem(ref items, out span) && uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var openedFileOrDirectoryAttributesValue) &&
                tryGetNextItem(ref items, out span) && uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var absolutePathValue))
            {
                if (statusValue > (uint)FileAccessStatus.CannotDeterminePolicy)
                {
                    errorMessage = I($"Unknown file access status '{statusValue}'");
                    return false;
                }

                if (requestedAccessValue > (uint)RequestedAccess.All)
                {
                    errorMessage = I($"Unknown requested access '{requestedAccessValue}'");
                    return false;
                }

                requestedAccess = (RequestedAccess)requestedAccessValue;
                status = (FileAccessStatus)statusValue;
                explicitlyReported = explicitlyReportedValue != 0;
                desiredAccess = (DesiredAccess)desiredAccessValue;
                shareMode = (ShareMode)shareModeValue;
                creationDisposition = (CreationDisposition)creationDispositionValue;
                flagsAndAttributes = (FlagsAndAttributes)flagsAndAttributesValue;
                openedFileOrDirectoryAttributes = (FlagsAndAttributes)openedFileOrDirectoryAttributesValue;
                absolutePath = new AbsolutePath(unchecked((int)absolutePathValue));
                if (!tryGetNextItem(ref items, out span))
                {
                    tryGetErrorMessage(line, operation, items.MatchCount, out errorMessage);
                    return false;
                }

                path = span.ToString();
                // Detours is only guaranteed to sent at least 12 items, so here (since we are at index 12), we must check if this item is included

                enumeratePattern = tryGetNextItem(ref items, out span) ? span.ToString() : null;

                if (requestedAccess != RequestedAccess.Enumerate)
                {
                    // If the requested access is not enumeration, enumeratePattern does not matter.
                    enumeratePattern = null;
                }

                if ((operation == ReportedFileOperation.Process) && tryGetNextItem(ref items, out span))
                {
                    processArgs = span.ToString();
                    while (items.MoveNext())
                    {
                        processArgs += "|";
                        processArgs += items.GetCurrent().ToString();
                    }
                }
                else
                {
                    processArgs = string.Empty;
                }

                usn = new Usn(usnValue);
                Contract.Assert(index <= items.MatchCount);
                return true;
            }

            // Trying to get error message if we failed to parse because the input string doesn't have enough sections.
            tryGetErrorMessage(line, operation, items.MatchCount, out errorMessage);

            return false;

            static bool tryGetNextItem(
                ref SpanSplitEnumerator<char> items,
#if NET5_0_OR_GREATER
                // Span-based parsing is only supported in .net core
                out ReadOnlySpan<char> result)
#else
                out string result)
#endif
            {
#if NET5_0_OR_GREATER
                return items.TryGetNextItem(out result);
#else
                result = default!;
                if (items.TryGetNextItem(out var span))
                {
                    result = span.ToString();
                    return true;
                }
                
                return false;
#endif
            }

            void tryGetErrorMessage(string line, ReportedFileOperation operation, int itemsLength, out string? errorMessage)
            {
                // Make sure the formatting happens only if the condition is false.
                if (itemsLength < MinItemsCount)
                {
                    // When the command line arguments of the process are not reported there will be 12 fields
                    // When command line arguments are included, everything after the 12th field is the command line argument
                    // Command line arguments are only reported when the reported file operation is Process
                    if (operation == ReportedFileOperation.Process)
                    {
                        errorMessage = I($"Unexpected message items (potentially due to pipe corruption) for {operation.ToString()} operation. Message '{line}'. Expected >= {MinItemsCount} items, Received {itemsLength} items");
                    }
                    else
                    {
                        // An ill behaved tool can try to do GetFileAttribute on a file with '|' char. This will result in a failure of the API, but we get a report for the access.
                        // Allow that by handling such case.
                        // In Office build there is a call to GetFileAttribute with a small xml document as a file name.
                        errorMessage = I($"Unexpected message items (potentially due to pipe corruption) for {operation.ToString()} operation. Message '{line}'. Expected >= {MinItemsCount} items, Received {itemsLength} items");
                    }
                }
                else
                {
                    errorMessage = null;
                }
            }
        }
    }
}
