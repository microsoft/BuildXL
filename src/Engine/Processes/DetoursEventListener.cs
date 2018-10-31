// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        /// Version of the interface
        /// </summary>
        public const uint Version = 1;

        // By default the handling is set store the data in the SandboxedProcessReports collection only.
        private MessageHandlingFlags m_messageHandlingFlags = MessageHandlingFlags.FileAccessCollect | MessageHandlingFlags.ProcessDataCollect | MessageHandlingFlags.ProcessDetoursStatusCollect;

        /// <summary>
        /// Called to handle FileAccess message.
        /// </summary>
        /// <param name="pipId">The pip id</param>
        /// <param name="pipDescription">The pip descruption</param>
        /// <param name="operation">The operation</param>
        /// <param name="requestedAccess">The requested access</param>
        /// <param name="status">The status of the access request</param>
        /// <param name="explicitlyReported">Is it an explicit report</param>
        /// <param name="processId">The process ID that made the access.</param>
        /// <param name="error">Error code of the operation</param>
        /// <param name="desiredAccess">The desired access</param>
        /// <param name="shareMode">The share mode</param>
        /// <param name="creationDisposition">The creation disposition</param>
        /// <param name="flagsAndAttributes">The flags and attributes</param>
        /// <param name="path">The path being accessed</param>
        /// <param name="processArgs">The process arguments</param>
        public abstract void HandleFileAccess(
            long pipId,
            string pipDescription,
            ReportedFileOperation operation,
            RequestedAccess requestedAccess,
            FileAccessStatus status,
            bool explicitlyReported,
            uint processId,
            uint error,
            DesiredAccess desiredAccess,
            ShareMode shareMode,
            CreationDisposition creationDisposition,
            FlagsAndAttributes flagsAndAttributes,
            string path,
            string processArgs);

        /// <summary>
        /// Called to handle a debug message from detours.
        /// </summary>
        /// <param name="pipId">The pip id.</param>
        /// <param name="pipDescription">The pip description</param>
        /// <param name="debugMessage">The debug message</param>
        public abstract void HandleDebugMessage(
            long pipId,
            string pipDescription,
            string debugMessage);

        /// <summary>
        /// Called to handle process data.
        /// </summary>
        /// <param name="pipId">The pip id</param>
        /// <param name="pipDescription">The pip description</param>
        /// <param name="processName">The process name</param>
        /// <param name="processId">The process id</param>
        /// <param name="creationDateTime">The creation date and time</param>
        /// <param name="exitDateTime">The exit date</param>
        /// <param name="kernelTime">The kernel time</param>
        /// <param name="userTime">The user time</param>
        /// <param name="exitCode">The exit code</param>
        /// <param name="ioCounters">The IO Counters for the process</param>
        /// <param name="parentProcessId">The parent process id</param>
        public abstract void HandleProcessData(
            long pipId,
            string pipDescription,
            string processName,
            uint processId,
            DateTime creationDateTime,
            DateTime exitDateTime,
            TimeSpan kernelTime,
            TimeSpan userTime,
            uint exitCode,
            BuildXL.Pips.IOCounters ioCounters,
            uint parentProcessId);

        /// <summary>
        /// Called to handle detouring status message.
        /// </summary>
        /// <param name="processId">The process id</param>
        /// <param name="reportStatus">The report status</param>
        /// <param name="processName">The process name</param>
        /// <param name="startApplicationName">The application name</param>
        /// <param name="startCommandLine">The app command line</param>
        /// <param name="needsInjection">Whether the process needed injection</param>
        /// <param name="hJob">The process job handle</param>
        /// <param name="disableDetours">Whether detours was disabled</param>
        /// <param name="creationFlags">The creation flags</param>
        /// <param name="detoured">Whether the process was detoured</param>
        /// <param name="error">The error of the creation of a process.</param>
        /// <param name="createProcessStatusReturn">The returned status for the detoured process creation</param>
        public abstract void HandleProcessDetouringStatus(
            ulong processId,
            uint reportStatus,
            string processName,
            string startApplicationName,
            string startCommandLine,
            bool needsInjection,
            ulong hJob,
            bool disableDetours,
            uint creationFlags,
            bool detoured,
            uint error,
            uint createProcessStatusReturn);

        /// <summary>
        /// Gets the flags that are used to handle different message types
        /// </summary>
        /// <returns>The message handling flags</returns>
        /// <remarks>By default the handling is set store the data in the SandboxedProcessReports collection only.</remarks>
        public MessageHandlingFlags GetMessageHandlingFlags()
        {
            return m_messageHandlingFlags;
        }

        /// <summary>
        /// Sets the flags that are used to handle different message types
        /// </summary>
        /// <param name="flags">The message handling flags</param>
        /// <remarks>By default the handling is set store the data in the SandboxedProcessReports collection only.</remarks>
        public void SetMessageHandlingFlags(MessageHandlingFlags flags)
        {
            m_messageHandlingFlags = flags;
        }
    }
}
