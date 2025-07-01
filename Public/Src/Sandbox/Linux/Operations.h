// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef BUILDXL_SANDBOX_LINUX_OPERATIONS_H
#define BUILDXL_SANDBOX_LINUX_OPERATIONS_H

namespace buildxl {
namespace linux {

/**
 * Represents the type of event that was intercepted by the sandbox.
 */
enum class EventType {
    kClone = 0,
    kPTrace,
    kFirstAllowWriteCheckInProcess,
    kExec,
    kExit,
    kOpen,
    kClose,
    kCreate,
    kGenericWrite,
    kGenericRead,
    kGenericProbe,
    kRename,
    kReadLink,
    kLink,
    kUnlink,
    kBreakAway,
    kMax // Not a valid event type
};

/**
 * Represents the a file operation that was determined by the information collected by the sandbox along with the EventType.
 * This is sent back to the managed side.
 * This enum contains a subset of the operations in the following files:
 * CODESYNC: Public/Src/Engine/Processes/ReportedFileOperation.cs
 * CODESYNC: Public/Src/Engine/Processes/FileAccessReportLine.cs
 */
enum class FileOperation {
    kProcess = 0,
    kProcessExec,
    kProcessRequiresPtrace,
    kFirstAllowWriteCheckInProcess,
    kProcessExit,
    kReadlink,
    kCreateFile,
    kReadFile,
    kWriteFile,
    kDeleteFile,
    kCreateHardlinkSource,
    kCreateHardlinkDest,
    kOpenDirectory,
    kCreateDirectory,
    kRemoveDirectory,
    kClose,
    kProbe,
    kProcessBreakaway,
    kMax,
};

/**
 * The severity of a debug message sent to the managed code.
 * 
 * CODESYNC: Public/Src/Engine/Processes/SandboxReportLinux.cs
 */
enum class DebugEventSeverity {
    kDebug = 0,
    kInfo = 1,
    kWarning = 2,
    kError = 3,
};

} // namespace linux
} // namespace buildxl

#endif // BUILDXL_SANDBOX_LINUX_OPERATIONS_H