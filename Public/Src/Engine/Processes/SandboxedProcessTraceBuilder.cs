// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BuildXL.Native.IO;
using BuildXL.Utilities.Core;

namespace BuildXL.Processes
{
    /// <summary>
    /// A helper class to create a trace file from observations reported by the sandbox.
    /// </summary>
    /// <remarks>
    /// This class builds a trace file from observations reported by the sandbox. The traces are written in a scheme/format that produces a compact representation of the data.
    /// Unfortunately, changing the format of the trace file may break compatibility with existing tools that consume the trace file. Worse, the trace file can be part of
    /// a pip's outputs, and can be consumed (read, parsed, etc.) by other pips. Thus, changing the format of the trace file may break builds.
    ///
    /// TODO: Address this compatibility isssue. Some possible solutions are:
    ///     1. Use a format that allows for backward compatibility, like protobuf.
    ///     2. Create a tool to read and parse the trace file, and let customers use that tool to consume the trace file.
    ///        Then ensure that the customer use the same version of the tool and the BuildXL in their builds.
    ///        This way, BuildXL developers can change the format of the trace file without breaking the builds.
    /// </remarks>
    internal sealed class SandboxedProcessTraceBuilder
    {
        private const byte Version = 1;

        private static readonly ObjectPool<HashSet<ReportedFileOperation>> s_reportedFileOperationSetPool = Pools.CreateSetPool<ReportedFileOperation>();

        private static readonly ObjectPool<HashSet<RequestedAccess>> s_requestedAccessSetPool = Pools.CreateSetPool<RequestedAccess>();

        private readonly ISandboxedProcessFileStorage m_fileStorage;

        private readonly PathTable m_pathTable;

        private readonly List<Operation> m_operations = [];

        private readonly List<ReportedProcess> m_reportedProcesses = [];

        private uint m_fileAccessCounter;

        private int m_fileHasBeenSaved;

        /// <summary>
        /// Number of recorded operations.
        /// </summary>
        public int OperationCount => m_operations.Count;

        /// <summary>
        /// Number of reported processes.
        /// </summary>
        public int ReportedProcessCount => m_reportedProcesses.Count;

        /// <summary>
        /// Constructor.
        /// </summary>
        public SandboxedProcessTraceBuilder(ISandboxedProcessFileStorage fileStorage, PathTable pathTable)
        {
            Contract.Requires(!string.IsNullOrEmpty(fileStorage.GetFileName(SandboxedProcessFile.Trace)));
            Contract.Requires(pathTable != null);

            m_fileStorage = fileStorage;
            m_pathTable = pathTable;
        }

        /// <summary>
        /// Freezes the trace and returns the output.
        /// </summary>
        public SandboxedProcessOutput Freeze()
        {
            string file = m_fileStorage.GetFileName(SandboxedProcessFile.Trace);
            Encoding encoding = Encoding.UTF8;

            try
            {
                FileUtilities.CreateDirectory(Path.GetDirectoryName(file));
                using FileStream stream = FileUtilities.CreateReplacementFile(
                    file,
                    FileShare.Read | FileShare.Delete,
                    openAsync: false);
                using var writer = new StreamWriter(stream, encoding);

                WriteToStream(writer);

                return new SandboxedProcessOutput(stream.Length, null, file, encoding, m_fileStorage, SandboxedProcessFile.Trace, null);
            }
            catch (Exception ex)
            {
                return new SandboxedProcessOutput(
                    SandboxedProcessOutput.NoLength,
                    value: null,
                    fileName: null,
                    encoding,
                    m_fileStorage,
                    SandboxedProcessFile.Trace,
                    new BuildXLException("An exception occurred while saving a trace file", innerException: ex));
            }
        }

        /// <summary>
        /// Reports a single detours observation.
        /// </summary>
        /// <remarks>
        /// The builder will decide whether it should be recorded or not.
        /// </remarks>
        public void ReportFileAccess(
            uint processId,
            ReportedFileOperation operation,
            RequestedAccess requestedAccess,
            AbsolutePath path,
            uint error,
            bool isAnAugmentedFileAccess,
            string enumeratePattern)
        {
            if (SkipOperation(operation))
            {
                return;
            }

            m_operations.Add(new Operation
            {
                Id = m_fileAccessCounter++,
                ProcessId = processId,
                Error = error,
                Path = path,
                FileOperation = operation,
                RequestedAccess = requestedAccess,
                IsAnAugmentedFileAccess = isAnAugmentedFileAccess,
                EnumeratePattern = enumeratePattern
            });
        }

        /// <summary>
        /// Reports process.
        /// </summary>
        public void ReportProcess(ReportedProcess process)
        {
            m_reportedProcesses.Add(process);
        }

        /// <summary>
        /// Updates the arguments of a process that was already reported.
        /// </summary>
        public void UpdateProcessArgs(ReportedProcess process, string path, string args)
        {
            var matchingProcess = m_reportedProcesses.FirstOrDefault(p => p.ProcessId == process.ProcessId);
            if (matchingProcess != default)
            {
                matchingProcess.UpdateOnPathAndArgsOnExec(path, args);
            }
        }

        private static bool SkipOperation(ReportedFileOperation operation)
        {
            return operation switch
            {
                ReportedFileOperation.ChangedReadWriteToReadAccess
                or ReportedFileOperation.FirstAllowWriteCheckInProcess
                or ReportedFileOperation.ProcessRequiresPTrace
                or ReportedFileOperation.ProcessBreakaway => true,
                _ => false,
            };
        }

        /// <summary>
        /// Reads the trace from a stream.
        /// </summary>
        internal static (byte version, List<Operation>, List<ReportedProcess>) ReadFromStream(StreamReader reader, PathTable pathTableToVerify = null)
        {
            var reportedFileOperations = new Dictionary<byte, ReportedFileOperation>();
            var requestedAccesses = new Dictionary<byte, RequestedAccess>();
            var reportedProcesses = new List<ReportedProcess>();
            var absolutePaths = new Dictionary<AbsolutePath, string>();
            var operations = new List<Operation>();

            byte version = byte.Parse(reader.ReadLine());
            int reportedFileOperationCount = int.Parse(reader.ReadLine());
            for (int i = 0; i < reportedFileOperationCount; i++)
            {
                var parts = reader.ReadLine().Split('=');
                reportedFileOperations.Add(byte.Parse(parts[0]), (ReportedFileOperation)Enum.Parse(typeof(ReportedFileOperation), parts[1]));
            }

            int requestedAccessCount = int.Parse(reader.ReadLine());
            for (int i = 0; i < requestedAccessCount; i++)
            {
                var parts = reader.ReadLine().Split('=');
                requestedAccesses.Add(byte.Parse(parts[0]), (RequestedAccess)Enum.Parse(typeof(RequestedAccess), parts[1]));
            }

            int reportedProcessCount = int.Parse(reader.ReadLine());
            for (int i = 0; i < reportedProcessCount; i++)
            {
                var parts = reader.ReadLine().Split(',');
                var processId = uint.Parse(parts[0]);
                var path = parts[1].Trim('"');
                var parentProcessId = uint.Parse(parts[2]);
                var creationTime = new DateTime(long.Parse(parts[3]), DateTimeKind.Utc);
                var exitTime = new DateTime(long.Parse(parts[4]), DateTimeKind.Utc);
                var exitCode = uint.Parse(parts[5]);
                var processArgs = reader.ReadLine();
                reportedProcesses.Add(new ReportedProcess(processId, path, processArgs)
                {
                    ParentProcessId = parentProcessId,
                    CreationTime = creationTime,
                    ExitTime = exitTime,
                    ExitCode = exitCode
                });
            }

            int absolutePathCount = int.Parse(reader.ReadLine());
            for (int i = 0; i < absolutePathCount; i++)
            {
                var parts = reader.ReadLine().Split('=');
                var rawValue = int.Parse(parts[0]);
                var path = new AbsolutePath(rawValue);
                absolutePaths.Add(path, parts[1]);
                if (pathTableToVerify != null && path.IsValid)
                {
                    if (!AbsolutePath.TryGet(pathTableToVerify, parts[1], out AbsolutePath result) || result != path)
                    {
                        throw new BuildXLException($"The path '{parts[1]}' does not match any path id in the path table");
                    }
                }
            }

            int pathsCount = int.Parse(reader.ReadLine());
            Contract.Assert(pathsCount == 0, "Paths block is deprecated.");

            int operationCount = int.Parse(reader.ReadLine());
            for (int i = 0; i < operationCount; i++)
            { 
                // id, PID, Path, , FileOperation, RequestedAccess, Error, IsAnAugmentedFileAccess, EnumeratePattern{
                var parts = reader.ReadLine().Split(',');
                var id = uint.Parse(parts[0]);
                var processId = uint.Parse(parts[1]);
                var path = new AbsolutePath(int.Parse(parts[2]));
                var fileOperation = reportedFileOperations[byte.Parse(parts[4])];
                var requestedAccess = requestedAccesses[byte.Parse(parts[5])];
                var error = uint.Parse(parts[6]);
                var isAnAugmentedFileAccess = parts[7] == "1";
                var enumeratePattern = parts[8];
                operations.Add(new Operation
                {
                    Id = id,
                    ProcessId = processId,
                    Path = path,
                    FileOperation = fileOperation,
                    RequestedAccess = requestedAccess,
                    Error = error,
                    IsAnAugmentedFileAccess = isAnAugmentedFileAccess,
                    EnumeratePattern = enumeratePattern
                });
            }


            return (version, operations, reportedProcesses);
        }

        /// <summary>
        /// Writes the trace to a stream.
        /// </summary>
        /// <remarks>Changing the format may break existing customers' builds; see class remarks for details.</remarks>
        public void WriteToStream(StreamWriter writer)
        {
            //  Schema:
            //      Version number
            //      ReportedFileOperation block
            //          Count
            //          (byte)ReportedFileOperation=ReportedFileOperation
            //      RequestedAccess block
            //          Count
            //          (byte)RequestedAccess=RequestedAccess
            //      Process block
            //          Count
            //          ProcessId = ReportedProcess
            //      AbsolutePath block
            //          Count
            //          AbsolutePath.RawValue = AbsolutePath
            //      Paths block  <--- DEPRECATED
            //          Count
            //          m_paths[Path] = Path
            //      Operations
            //          Count
            //          Operation.Id = Operation

            Contract.Assert(Interlocked.CompareExchange(ref m_fileHasBeenSaved, 1, 0) == 0, "Trace file should be saved at most once.");

            using var pooledSb = Pools.GetStringBuilder();
            using var pooledAbsolutePathSet = Pools.GetAbsolutePathSet();
            using var pooledReportedFileOperationSet = s_reportedFileOperationSetPool.GetInstance();
            using var pooledRequestedAccessSet = s_requestedAccessSetPool.GetInstance();
            var sb = pooledSb.Instance;
            var absolutePaths = pooledAbsolutePathSet.Instance;
            var reportedFileOperations = pooledReportedFileOperationSet.Instance;
            var requestedAccesses = pooledRequestedAccessSet.Instance;

            writer.WriteLine(Version);

            foreach (var operation in m_operations)
            {
                absolutePaths.Add(operation.Path);
                reportedFileOperations.Add(operation.FileOperation);
                requestedAccesses.Add(operation.RequestedAccess);
            }

            writer.WriteLine(reportedFileOperations.Count);
            foreach (var fileOperation in reportedFileOperations)
            {
                writer.WriteLine($"{(byte)fileOperation}={fileOperation}");
            }

            writer.WriteLine(requestedAccesses.Count);
            foreach (var requestedAccess in requestedAccesses)
            {
                writer.WriteLine($"{(byte)requestedAccess}={requestedAccess:G}");
            }

            writer.WriteLine(m_reportedProcesses.Count);
            foreach (var process in m_reportedProcesses)
            {
                formatReportedProcess(process, sb);
#if NETCOREAPP
                writer.WriteLine(sb);
#else
                writer.WriteLine(sb.ToString());
#endif
                sb.Clear();
            }

            writer.WriteLine(absolutePaths.Count);
            foreach (var absolutePath in absolutePaths.OrderBy(x => x.RawValue))
            {
                writer.WriteLine($"{absolutePath.RawValue}={absolutePath.ToString(m_pathTable)}");
            }

            // Paths block is deprecated
            // This count number is added to maintain compatibility with the existing format.
            writer.WriteLine(0);

            writer.WriteLine(m_operations.Count);
            foreach (var operation in m_operations)
            {
                formatOperation(operation, sb);
#if NETCOREAPP3_0_OR_GREATER
                writer.WriteLine(sb);
#else
                writer.WriteLine(sb.ToString());
#endif
                sb.Clear();
            }


            static void formatReportedProcess(ReportedProcess process, StringBuilder sb)
            {
                // PID, "path", ParentPID, startTimeUtcTicks, endTimeUtcTicks
                // CommandLineArgs
                sb.Append($"{process.ProcessId},\"{process.Path}\",{process.ParentProcessId},");
                sb.AppendLine($"{process.CreationTime.ToUniversalTime().Ticks},{process.ExitTime.ToUniversalTime().Ticks},{process.ExitCode}");
                sb.Append(process.ProcessArgs);
            }

            static void formatOperation(Operation operation, StringBuilder sb)
            {
                // id, PID, Path,, FileOperation, RequestedAccess, Error, IsAnAugmentedFileAccess, EnumeratePattern
                // Note that there is an empty field between Path and FileOperation to maintain compatibility with the existing format.
                sb.Append($"{operation.Id},{operation.ProcessId},");
                sb.Append($"{operation.Path.RawValue},,");
                sb.Append($"{(byte)operation.FileOperation},{(byte)operation.RequestedAccess},{operation.Error},{(operation.IsAnAugmentedFileAccess ? 1 : 0)},{operation.EnumeratePattern}");
            }
        }

        internal readonly struct Operation
        {
            public uint Id { get; init; }
            public uint ProcessId { get; init; }
            public uint Error { get; init; }
            public ReportedFileOperation FileOperation { get; init; }
            public RequestedAccess RequestedAccess { get; init; }
            public bool IsAnAugmentedFileAccess { get; init; }
            public AbsolutePath Path { get; init; }
            public string EnumeratePattern { get; init; }
        }
    }
}
