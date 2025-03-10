// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Processes
{
    /// <summary>
    /// Severity of a debug event.
    /// </summary>
    /// <remarks>
    /// CODESYNC: Public/Src/Sandbox/Linux/Operations.h
    /// </remarks>
    public enum DebugEventSeverity
    {
        /// <nodoc />
        Info = 0,
        
        /// <nodoc />
        Warning = 1,
        
        /// <nodoc />
        Error = 2,
    }

    /// <summary>
    /// Represents a parsed report from the Linux Sandbox.
    /// </summary>
    public struct SandboxReportLinux
    {
        /// <summary>
        /// The type of report sent back that will specify how the report should be interpreted.
        /// </summary>
        public ReportType ReportType;

        /// <summary>
        /// The name of the system call that generated this report.
        /// </summary>
        public string SystemCall;

        /// <summary>
        /// The reported file operation.
        /// </summary>
        public ReportedFileOperation FileOperation;

        /// <summary>
        /// The process id of the process that generated this report.
        /// </summary>
        /// <remarks>
        /// On a fork/clone event, this will be the child process id.
        /// </remarks>
        public uint ProcessId;

        /// <summary>
        /// The parent process id of the process that generated this report.
        /// </summary>
        /// /// <remarks>
        /// On a fork/clone event, this will be the process id of the caller of fork/clone.
        /// </remarks>
        public uint ParentProcessId;

        /// <summary>
        /// If the report was an exec operation, then the command line arguments will be stored here.
        /// </summary>
        public string CommandLineArguments;

        /// <summary>
        /// If the system call failed and set errno, this will be the errno value.
        /// </summary>
        public uint Error;
        
        /// <summary>
        /// Represents a path if this is a file access report, or a message if this is a debug report.
        /// </summary>
        public string Data;

        /// <summary>
        /// Represents whether the reported path was a directory.
        /// </summary>
        public bool IsDirectory;

        /// <summary>
        /// The requested access for this report.
        /// </summary>
        public RequestedAccess RequestedAccess;

        /// <summary>
        /// The file access status for this report.
        /// </summary>
        public uint FileAccessStatus;

        /// <summary>
        /// Whether the sandbox indicated that this report should be explicitly reported.
        /// </summary>
        public uint ExplicitlyReport;

        /// <summary>
        /// Severity of the event applicable only for <see cref="ReportType.DebugMessage"/>. 
        /// </summary>
        public DebugEventSeverity Severity;
    }
}