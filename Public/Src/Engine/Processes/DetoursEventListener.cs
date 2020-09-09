// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Processes
{
    /// <summary>
    /// This enum specifies how messages are handled/collected.
    /// </summary>
    [Flags]
    public enum MessageHandlingFlags
    {
        /// <summary>
        /// Nothing specified.
        /// </summary>
        None = 0,

        /// <summary>
        /// The FileAccess message is notifying over the DetoursEventListener interface.
        /// </summary>
        FileAccessNotify = 1,

        /// <summary>
        /// The FileAccess message is collected in the FileAccesses, ExplicitlyReportedFileAccesses, FileUnexpectedAccesses collections in the SandboxedProcessReports class.
        /// </summary>
        FileAccessCollect = 1 << 1,

        /// <summary>
        /// The Debug message is notifying over the DetoursEventListener interface.
        /// </summary>
        DebugMessageNotify = 1 << 2,

        /// <summary>
        /// The ProcessData message is notifying over the DetoursEventListener interface.
        /// </summary>
        ProcessDataNotify = 1 << 3,

        /// <summary>
        /// The ProcessData message is collected in the Processes collection in the SandboxedProcessReports class.
        /// </summary>
        ProcessDataCollect = 1 << 4,

        /// <summary>
        /// The ProcessDetouringStatus message is notifying over the DetoursEventListener interface.
        /// </summary>
        ProcessDetoursStatusNotify = 1 << 5,

        /// <summary>
        /// The ProcessDetouringStatus message is collected in the ProcessDetoursStatuses collection in the SandboxedProcessReports class.
        /// </summary>
        ProcessDetoursStatusCollect = 1 << 6,
    }

    /// <summary>
    /// Interface for the DetoursEventListener protocol
    /// </summary>
    public abstract class IDetoursEventListener
    {
        /// <summary>
        /// File access data.
        /// </summary>
        public struct FileAccessData
        {
            /// <summary>
            /// Pip id.
            /// </summary>
            public long PipId { get; set; }
            
            /// <summary>
            /// Pip description.
            /// </summary>
            public string PipDescription { get; set; }

            /// <summary>
            /// Operation the performed the file access.
            /// </summary>
            public ReportedFileOperation Operation { get; set; }

            /// <summary>
            /// Requested access.
            /// </summary>
            public RequestedAccess RequestedAccess { get; set; }

            /// <summary>
            /// File access status.
            /// </summary>
            public FileAccessStatus Status { get; set; }

            /// <summary>
            /// True if file access is explicitly reported.
            /// </summary>
            public bool ExplicitlyReported { get; set; }
            
            /// <summary>
            /// Process id.
            /// </summary>
            public uint ProcessId { get; set; }

            /// <summary>
            /// Id of file access.
            /// </summary>
            public uint Id { get; set; }

            /// <summary>
            /// Correlation id of file access.
            /// </summary>
            public uint CorrelationId { get; set; }
            
            /// <summary>
            /// Error code of the operation.
            /// </summary>
            public uint Error { get; set; }

            /// <summary>
            /// Desired access.
            /// </summary>
            public DesiredAccess DesiredAccess { get; set; }
            
            /// <summary>
            /// Requested sharing mode.
            /// </summary>
            public ShareMode ShareMode { get; set; }

            /// <summary>
            /// Create disposition, i.e., action to take on file that exists or does not exist.
            /// </summary>
            public CreationDisposition CreationDisposition { get; set; }
            
            /// <summary>
            /// File flags and attributes.
            /// </summary>
            public FlagsAndAttributes FlagsAndAttributes { get; set; }
            
            /// <summary>
            /// Path being accessed.
            /// </summary>
            public string Path { get; set; }

            /// <summary>
            /// Process arguments.
            /// </summary>
            public string ProcessArgs { get; set; }

            /// <summary>
            /// Whether the file access is augmented.
            /// </summary>
            public bool IsAnAugmentedFileAccess { get; set; }
        }

        /// <summary>
        /// Process data.
        /// </summary>
        public struct ProcessData
        {
            /// <summary>
            /// Pip id.
            /// </summary>
            public long PipId { get; set; }

            /// <summary>
            /// Pip description.
            /// </summary>
            public string PipDescription { get; set; }

            /// <summary>
            /// Process name.
            /// </summary>
            public string ProcessName { get; set; }

            /// <summary>
            /// Process id.
            /// </summary>
            public uint ProcessId { get; set; }

            /// <summary>
            /// Parent process id.
            /// </summary>
            public uint ParentProcessId { get; set; }

            /// <summary>
            /// Creation date time.
            /// </summary>
            public DateTime CreationDateTime { get; set; }

            /// <summary>
            /// Exit date time.
            /// </summary>
            public DateTime ExitDateTime { get; set; }

            /// <summary>
            /// Kernel time.
            /// </summary>
            public TimeSpan KernelTime { get; set; }

            /// <summary>
            /// User name.
            /// </summary>
            public TimeSpan UserTime { get; set; }
            
            /// <summary>
            /// Exit code.
            /// </summary>
            public uint ExitCode { get; set; }
            
            /// <summary>
            /// IO counters.
            /// </summary>
            public Native.IO.IOCounters IoCounters { get; set; }
        }

        /// <summary>
        /// Debug data.
        /// </summary>
        public struct DebugData
        {
            /// <summary>
            /// Pip id.
            /// </summary>
            public long PipId { get; set; }

            /// <summary>
            /// Pip description.
            /// </summary>
            public string PipDescription { get; set; }

            /// <summary>
            /// Debug message.
            /// </summary>
            public string DebugMessage { get; set; }
        }

        /// <summary>
        /// Version of the interface
        /// </summary>
        /// <remarks>
        /// 2: Wrap individual arguments of API into structs to avoid breaking changes when adding a new argument.
        /// </remarks>
        public const uint Version = 2;

        // By default the handling is set store the data in the SandboxedProcessReports collection only.
        private MessageHandlingFlags m_messageHandlingFlags = MessageHandlingFlags.FileAccessCollect | MessageHandlingFlags.ProcessDataCollect | MessageHandlingFlags.ProcessDetoursStatusCollect;

        /// <summary>
        /// Called to handle FileAccess message.
        /// </summary>
        /// <param name="fileAccessData">File access data.</param>
        public abstract void HandleFileAccess(FileAccessData fileAccessData);

        /// <summary>
        /// Called to handle a debug message from detours.
        /// </summary>
        /// <param name="debugData">Debug data.</param>
        public abstract void HandleDebugMessage(DebugData debugData);

        /// <summary>
        /// Called to handle process data.
        /// </summary>
        /// <param name="processData">Process data.</param>
        public abstract void HandleProcessData(ProcessData processData);

        /// <summary>
        /// Called to handle detouring status message.
        /// </summary>
        public abstract void HandleProcessDetouringStatus(ProcessDetouringStatusData data);

        /// <summary>
        /// Gets the flags that are used to handle different message types
        /// </summary>
        /// <returns>The message handling flags</returns>
        /// <remarks>By default the handling is set store the data in the SandboxedProcessReports collection only.</remarks>
        public MessageHandlingFlags GetMessageHandlingFlags() => m_messageHandlingFlags;

        /// <summary>
        /// Sets the flags that are used to handle different message types
        /// </summary>
        /// <param name="flags">The message handling flags</param>
        /// <remarks>By default the handling is set store the data in the SandboxedProcessReports collection only.</remarks>
        public void SetMessageHandlingFlags(MessageHandlingFlags flags) => m_messageHandlingFlags = flags;
    }
}
