// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using BuildXL.Utilities;
using BuildXL.Interop.MacOS;
using BuildXL.Native.IO;
using static BuildXL.Utilities.FormattableStringEx;
using JetBrains.Annotations;

namespace BuildXL.Processes
{
    /// <summary>
    /// Low-level parser for file access report lines
    /// </summary>
    internal static class FileAccessReportLine
    {
        /// <summary>
        /// Mapping of "operation name" to <see cref="ReportedFileOperation"/>
        /// </summary>
        internal static readonly Dictionary<string, ReportedFileOperation> Operations =
            new Dictionary<string, ReportedFileOperation>(StringComparer.Ordinal)
            {
                    { "CreateFile", ReportedFileOperation.CreateFile },
                    { "CreateDirectory", ReportedFileOperation.CreateDirectory },
                    { "RemoveDirectory", ReportedFileOperation.RemoveDirectory },
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
                    { "ChangedReadWriteToReadAccess", ReportedFileOperation.ChangedReadWriteToReadAccess },
                    { "FirstAllowWriteCheckInProcess", ReportedFileOperation.FirstAllowWriteCheckInProcess },
                    { "MoveFileWithProgress_Source", ReportedFileOperation.MoveFileWithProgressSource },
                    { "MoveFileWithProgress_Dest", ReportedFileOperation.MoveFileWithProgressDest },
                    { "MultipleOperations", ReportedFileOperation.MultipleOperations },
                    { "CreateProcess", ReportedFileOperation.CreateProcess },
                    { FileOperation.OpMacLookup.GetName(), ReportedFileOperation.MacLookup },
                    { FileOperation.OpMacReadlink.GetName(), ReportedFileOperation.MacReadlink },
                    { FileOperation.OpMacVNodeCreate.GetName(), ReportedFileOperation.MacVNodeCreate },
                    { FileOperation.OpMacVNodeWrite.GetName(), ReportedFileOperation.MacVNodeWrite },
                    { FileOperation.OpMacVNodeCloneSource.GetName(), ReportedFileOperation.MacVNodeCloneSource },
                    { FileOperation.OpMacVNodeCloneDest.GetName(), ReportedFileOperation.MacVNodeCloneDest },
                    { FileOperation.OpKAuthMoveSource.GetName(), ReportedFileOperation.KAuthMoveSource },
                    { FileOperation.OpKAuthMoveDest.GetName(), ReportedFileOperation.KAuthMoveDest },
                    { FileOperation.OpKAuthCreateHardlinkSource.GetName(), ReportedFileOperation.KAuthCreateHardlinkSource },
                    { FileOperation.OpKAuthCreateHardlinkDest.GetName(), ReportedFileOperation.KAuthCreateHardlinkDest },
                    { FileOperation.OpKAuthCopySource.GetName(), ReportedFileOperation.KAuthCopySource },
                    { FileOperation.OpKAuthCopyDest.GetName(), ReportedFileOperation.KAuthCopyDest },
                    { FileOperation.OpKAuthDeleteDir.GetName(), ReportedFileOperation.KAuthDeleteDir },
                    { FileOperation.OpKAuthDeleteFile.GetName(), ReportedFileOperation.KAuthDeleteFile },
                    { FileOperation.OpKAuthOpenDir.GetName(), ReportedFileOperation.KAuthOpenDir },
                    { FileOperation.OpKAuthReadFile.GetName(), ReportedFileOperation.KAuthReadFile },
                    { FileOperation.OpKAuthCreateDir.GetName(), ReportedFileOperation.KAuthCreateDir },
                    { FileOperation.OpKAuthWriteFile.GetName(), ReportedFileOperation.KAuthWriteFile },
                    { FileOperation.OpKAuthClose.GetName(), ReportedFileOperation.KAuthClose },
                    { FileOperation.OpKAuthCloseModified.GetName(), ReportedFileOperation.KAuthCloseModified },
                    { FileOperation.OpKAuthVNodeExecute.GetName(), ReportedFileOperation.KAuthVNodeExecute },
                    { FileOperation.OpKAuthVNodeWrite.GetName(), ReportedFileOperation.KAuthVNodeWrite },
                    { FileOperation.OpKAuthVNodeRead.GetName(), ReportedFileOperation.KAuthVNodeRead },
                    { FileOperation.OpKAuthVNodeProbe.GetName(), ReportedFileOperation.KAuthVNodeProbe },
            };

        /// <summary>
        /// Parses a string representing a file access
        /// </summary>
        [SuppressMessage("Microsoft.Globalization", "CA1305:CultureInfo.InvariantCulture")]
        internal static bool TryParse(
            ref string line,
            out uint processId,
            out ReportedFileOperation operation,
            out RequestedAccess requestedAccess,
            out FileAccessStatus status,
            out bool explicitlyReported,
            out uint error,
            out Usn usn,
            out DesiredAccess desiredAccess,
            out ShareMode shareMode,
            out CreationDisposition creationDisposition,
            out FlagsAndAttributes flagsAndAttributes,
            out AbsolutePath absolutePath,
            out string path,
            out string enumeratePattern,
            out string processArgs,
            out string errorMessage)
        {
            // TODO: Task 138817: Refactor passing and parsing of report data from native to managed code

            operation = ReportedFileOperation.Unknown;
            requestedAccess = RequestedAccess.None;
            status = FileAccessStatus.None;
            processId = error = 0;
            usn = default;
            explicitlyReported = false;
            desiredAccess = 0;
            shareMode = ShareMode.FILE_SHARE_NONE;
            creationDisposition = 0;
            flagsAndAttributes = 0;
            absolutePath = AbsolutePath.Invalid;
            path = null;
            enumeratePattern = null;
            processArgs = null;
            errorMessage = string.Empty;

            var i = line.IndexOf(':');
            var index = 0;

            if (i > 0)
            {
                var items = line.Substring(i + 1).Split('|');

                if (!Operations.TryGetValue(line.Substring(0, i), out operation))
                {
                    // We could consider the report line malformed in this case; but in practice it is easy to forget to update this parser
                    // after adding a new call. So let's be conservative about throwing the line out so long as we can parse the important bits to follow.
                    operation = ReportedFileOperation.Unknown;
                }

                // When the command line arguments of the process are not reported there will be 12 fields
                // When command line arguments are included, everything after the 12th field is the command line argument
                // Command line arguments are only reported when the reported file operation is Process
                if (operation == ReportedFileOperation.Process)
                {
                    // Make sure the formatting happens only if the condition is false.
                    if (items.Length < 12)
                    {
                        errorMessage = I($"Unexpected message items (potentially due to pipe corruption) for {operation.ToString()} operation. Message '{line}'. Expected >= 12 items, Received {items.Length} items");
                        return false;
                    }
                }
                else
                {
                    // An ill behaved tool can try to do GetFileAttribute on a file with '|' char. This will result in a failure of the API, but we get a report for the access.
                    // Allow that by handling such case.
                    // In Office build there is a call to GetFileAttribute with a small xml document as a file name.
                    if (items.Length < 12)
                    {
                        errorMessage = I($"Unexpected message items (potentially due to pipe corruption) for {operation.ToString()} operation. Message '{line}'. Expected >= 12 items, Received {items.Length} items");
                        return false;
                    }
                }

                if (
                    uint.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out processId) &&
                    uint.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var requestedAccessValue) &&
                    uint.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var statusValue) &&
                    uint.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var explicitlyReportedValue) &&
                    uint.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out error) &&
                    ulong.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var usnValue) &&
                    uint.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var desiredAccessValue) &&
                    uint.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var shareModeValue) &&
                    uint.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var creationDispositionValue) &&
                    uint.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var flagsAndAttributesValue) &&
                    uint.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var absolutePathValue))
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
                    absolutePath = new AbsolutePath(unchecked((int)absolutePathValue));
                    path = items[index++];
                    // Detours is only guaranteed to sent at least 12 items, so here (since we are at index 12), we must check if this item is included
                    enumeratePattern = index < items.Length ? items[index++] : null;

                    if (requestedAccess != RequestedAccess.Enumerate)
                    {
                        // If the requested access is not enumeration, enumeratePattern does not matter.
                        enumeratePattern = null;
                    }

                    if ((operation == ReportedFileOperation.Process) && (items.Length > index))
                    {
                        processArgs = items[index++];
                        while (index < items.Length)
                        {
                            processArgs += "|";
                            processArgs += items[index++];
                        }
                    }
                    else
                    {
                        processArgs = string.Empty;
                    }

                    usn = new Usn(usnValue);
                    Contract.Assert(index <= items.Length);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns a detours report line representing an augmented file access. 
        /// </summary>
        /// <remarks>
        /// The line format is compatible with a regular file access report line. This is not a hard requirement (since <see cref="ReportType.AugmentedFileAccess"/> 
        /// is used to distinguish this line from regular lines), but is convenient to use the same report line parser for all cases. So future changes 
        /// can happen here as long as they are kept in sync with <see cref="SandboxedProcessReports.TryParseAugmentedFileAccess"/>
        /// </remarks>
        internal static string GetReportLineForAugmentedFileAccess(
            ReportedFileOperation reportedFileOperation, 
            uint processId, 
            RequestedAccess requestedAccess, 
            FileAccessStatus fileAccessStatus, 
            uint errorCode, 
            Usn usn, 
            DesiredAccess desiredAccess, 
            ShareMode shareMode,
            CreationDisposition creationDisposition,
            FlagsAndAttributes flagsAndAttributes,
            string absolutePath,
            [CanBeNull]string enumeratePattern,
            [CanBeNull]string processArgs)
        {
            var result = new System.Text.StringBuilder();

            result.Append($"{(int)ReportType.AugmentedFileAccess},{reportedFileOperation}:");
            result.Append($"{processId.ToString("x")}|");
            result.Append($"{((byte)requestedAccess).ToString("x")}|");
            result.Append($"{((byte)fileAccessStatus).ToString("x")}|");
            // '1' makes the access look as explicitly reported, but this actually doesn't matter since it will get
            // set based on the manifest policy upon reception
            result.Append("1|");
            result.Append($"{errorCode.ToString("x")}|");
            result.Append($"{usn.Value.ToString("x")}|");
            result.Append($"{((uint)desiredAccess).ToString("x")}|");
            result.Append($"{((uint)shareMode).ToString("x")}|");
            result.Append($"{((uint)creationDisposition).ToString("x")}|");
            result.Append($"{((uint)flagsAndAttributes).ToString("x")}|");
            // The manifest path is always written as invalid
            result.Append($"{AbsolutePath.Invalid.Value.Value.ToString("x")}|");
            result.Append(absolutePath);

            if (!string.IsNullOrEmpty(enumeratePattern))
            {
                Contract.Assert(requestedAccess == RequestedAccess.Enumerate);
                result.Append($"|{enumeratePattern}");
            }

            if (!string.IsNullOrEmpty(processArgs))
            {
                Contract.Assert(reportedFileOperation == ReportedFileOperation.Process);
                result.Append($"|{processArgs}");
            }

            return result.Append("\r\n").ToString();
        }
    }
}
