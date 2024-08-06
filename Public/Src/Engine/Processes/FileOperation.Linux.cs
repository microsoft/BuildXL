// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Processes
{
    /// <summary>
    /// Helper class for converting between the Linux Sandbox's file operation types and the global <see cref="ReportedFileOperation"/> type.
    /// </summary>
    public static class FileOperationLinux
    {
        /// <summary>
        /// Represents a file operation reported by the Linux Sandbox.
        /// CODESYNC: Public/Src/Sandbox/Linux/Operations.h
        /// TODO: Generate this with a roslyn source generator?
        /// </summary>
        public enum Operations : byte
        {
            /// <summary>
            /// Fork/Clone system calls.
            /// </summary>
            Process = 0,
            /// <summary>
            /// exec* system calls.
            /// </summary>
            ProcessExec,
            /// <summary>
            /// Pseudo operation to indicate that the process requires ptrace to be enabled.
            /// </summary>
            ProcessRequiresPtrace,
            /// <summary>
            /// Pseudo operation <see cref="ReportedFileOperation.FirstAllowWriteCheckInProcess"/> for details.
            /// </summary>
            FirstAllowWriteCheckInProcess,
            /// <summary>
            /// exit and _exit system calls.
            /// </summary>
            ProcessExit,
            /// <summary>
            /// readlink system call.
            /// </summary>
            Readlink,
            /// <summary>
            /// Generic create file system call.
            /// </summary>
            CreateFile,
            /// <summary>
            /// Generic read file system call.
            /// </summary>
            ReadFile,
            /// <summary>
            /// Generic write file system call.
            /// </summary>
            WriteFile,
            /// <summary>
            /// Generic delete file system call.
            /// </summary>
            DeleteFile,
            /// <summary>
            /// Generic create hardlink source system call.
            /// </summary>
            CreateHardlinkSource,
            /// <summary>
            /// Generic create hardlink destination system call.
            /// </summary>
            CreateHardlinkDest,
            /// <summary>
            /// Open directory with opendir, fdopendir.
            /// </summary>
            OpenDirectory,
            /// <summary>
            /// Create directory with mkdir or write.
            /// </summary>
            CreateDirectory,
            /// <summary>
            /// Delete directory with rmdir or unlink.
            /// </summary>
            RemoveDirectory,
            /// <summary>
            /// close system call.
            /// </summary>
            Close,
            /// <summary>
            /// Generic probe operation.
            /// </summary>
            Probe,
            /// <summary>
            /// Maximum value of the enum.
            /// </summary>
            Max,
        }

        /// <summary>
        /// Converts the <see cref="Operations"/> type from the native Linux Sandbox to the global <see cref="ReportedFileOperation"/> type.
        /// </summary>
        public static ReportedFileOperation ToReportedFileOperation(Operations op)
        {
            return op switch
            {
                Operations.Process => ReportedFileOperation.Process,
                Operations.ProcessExec => ReportedFileOperation.ProcessExec,
                Operations.ProcessRequiresPtrace => ReportedFileOperation.ProcessRequiresPTrace,
                Operations.FirstAllowWriteCheckInProcess => ReportedFileOperation.FirstAllowWriteCheckInProcess,
                Operations.ProcessExit => ReportedFileOperation.ProcessExit,
                Operations.Readlink => ReportedFileOperation.Readlink,
                Operations.CreateFile => ReportedFileOperation.CreateFile,
                Operations.ReadFile => ReportedFileOperation.ReadFile,
                Operations.WriteFile => ReportedFileOperation.WriteFile,
                Operations.DeleteFile => ReportedFileOperation.DeleteFile,
                Operations.CreateHardlinkSource => ReportedFileOperation.CreateHardlinkSource,
                Operations.CreateHardlinkDest => ReportedFileOperation.CreateHardlinkDest,
                Operations.OpenDirectory => ReportedFileOperation.OpenDirectory,
                Operations.CreateDirectory => ReportedFileOperation.CreateDirectory,
                Operations.RemoveDirectory => ReportedFileOperation.RemoveDirectory,
                Operations.Close => ReportedFileOperation.Close,
                Operations.Probe => ReportedFileOperation.Probe,
                _ => throw new Exception($"Unsupported operation {op}"),
            };
        }
    }
}
