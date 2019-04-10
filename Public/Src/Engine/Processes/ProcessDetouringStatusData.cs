// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// A (nested) detouring status of a process instance reported via Detours
    /// </summary>
    public sealed class ProcessDetouringStatusData
    {
        /// <summary>
        /// Process Id
        /// </summary>
        public ulong ProcessId { get; private set; }

        /// <summary>
        /// Report Status
        /// </summary>
        public uint ReportStatus { get; private set; }

        /// <summary>
        /// Process name
        /// </summary>
        public string ProcessName { get; private set; }

        /// <summary>
        /// Application being started.
        /// </summary>
        public string StartApplicationName { get; private set; }

        /// <summary>
        /// Command line for the process being started.
        /// </summary>
        public string StartCommandLine { get; private set; }

        /// <summary>
        /// Whether detours to be injected in the process.
        /// </summary>
        public bool NeedsInjection { get; private set; }

        /// <summary>
        /// Job Id
        /// </summary>
        public ulong Job { get; private set; }

        /// <summary>
        /// Whether detours is disabled.
        /// </summary>
        public bool DisableDetours { get; private set; }

        /// <summary>
        /// Process creation flags.
        /// </summary>
        public uint CreationFlags { get; set; }

        /// <summary>
        /// Whether the process was detoured.
        /// </summary>
        public bool Detoured { get; private set; }

        /// <summary>
        /// Error code that Detoured Process sets.
        /// </summary>
        public uint Error { get; private set; }

        /// <summary>
        /// Status returned from the the detoured CreateProcess function.
        /// </summary>
        public uint CreateProcessStatusReturn { get; private set; }

        /// <summary>
        /// Creates an instance of this class.
        /// </summary>
        /// <param name="processId">Process Id.</param>
        /// <param name="reportStatus">The detouring status.</param>
        /// <param name="processName">The process name of the process starting the new process.</param>
        /// <param name="startApplicationName">The application being started..</param>
        /// <param name="startCommandLine">The command line for the application being started.</param>
        /// <param name="needsInjection">Whether the application needs injection.</param>
        /// <param name="job">The job id that this process is associated with.</param>
        /// <param name="disableDetours">Whether detours is disabled.</param>
        /// <param name="creationFlags">The process creation flags.</param>
        /// <param name="detoured">Whether the process was detoured.</param>
        /// <param name="error">The last error that this create process sets.</param>
        /// <param name="createProcessStatusReturn">The return status of the detoured CreateProcess function.</param>
        public ProcessDetouringStatusData(
            ulong processId,
            uint reportStatus,
            string processName,
            string startApplicationName,
            string startCommandLine,
            bool needsInjection,
            ulong job,
            bool disableDetours,
            uint creationFlags,
            bool detoured,
            uint error,
            uint createProcessStatusReturn)
        {
            ProcessId = processId;
            ReportStatus = reportStatus;
            ProcessName = processName;
            StartApplicationName = startApplicationName;
            StartCommandLine = startCommandLine;
            NeedsInjection = needsInjection;
            Job = job;
            DisableDetours = disableDetours;
            CreationFlags = creationFlags;
            Detoured = detoured;
            Error = error;
            CreateProcessStatusReturn = createProcessStatusReturn;
        }

        /// <nodoc />
        public static ProcessDetouringStatusData Deserialize(BuildXLReader reader)
        {
            return new ProcessDetouringStatusData(
                processId: reader.ReadUInt64(),
                reportStatus: reader.ReadUInt32(),
                processName: reader.ReadString(),
                startApplicationName: reader.ReadString(),
                startCommandLine: reader.ReadString(),
                needsInjection: reader.ReadBoolean(),
                job: reader.ReadUInt64(),
                disableDetours: reader.ReadBoolean(),
                creationFlags: reader.ReadUInt32(),
                detoured: reader.ReadBoolean(),
                error: reader.ReadUInt32(),
                createProcessStatusReturn: reader.ReadUInt32());
        }

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(ProcessId);
            writer.Write(ReportStatus);
            writer.Write(ProcessName);
            writer.Write(StartApplicationName);
            writer.Write(StartCommandLine);
            writer.Write(NeedsInjection);
            writer.Write(Job);
            writer.Write(DisableDetours);
            writer.Write(CreationFlags);
            writer.Write(Detoured);
            writer.Write(Error);
            writer.Write(CreateProcessStatusReturn);
        }
    }
}
