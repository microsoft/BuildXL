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
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// A helper class to create a trace file from observations reported by the sandbox.
    /// </summary>
    internal sealed class SandboxedProcessTraceBuilder
    {
        private const byte Version = 1;

        private static readonly ObjectPool<HashSet<ReportedFileOperation>> s_reportedFileOperationSetPool = Pools.CreateSetPool<ReportedFileOperation>();

        private static readonly ObjectPool<HashSet<RequestedAccess>> s_requestedAccessSetPool = Pools.CreateSetPool<RequestedAccess>();

        private readonly ISandboxedProcessFileStorage m_fileStorage;

        private readonly PathTable m_pathTable;

        internal readonly Dictionary<string, int> Paths;

        internal readonly List<Operation> Operations;

        private readonly List<ReportedProcess> m_reportedProcesses;

        private uint m_fileAccessCounter;

        private int m_fileHasBeenSaved;

        public SandboxedProcessTraceBuilder(ISandboxedProcessFileStorage fileStorage, PathTable pathTable)
        {
            Contract.Requires(!string.IsNullOrEmpty(fileStorage.GetFileName(SandboxedProcessFile.Trace)));
            Contract.Requires(pathTable != null);

            m_fileStorage = fileStorage;
            m_pathTable = pathTable;
            Paths = new Dictionary<string, int>(OperatingSystemHelper.PathComparer);
            Operations = new List<Operation>();
            m_reportedProcesses = new List<ReportedProcess>();
        }

        private SandboxedProcessTraceBuilder()
        {
            m_fileStorage = null; ;
            m_pathTable = null;
            Paths = new Dictionary<string, int>(OperatingSystemHelper.PathComparer);
            Operations = new List<Operation>();
            m_reportedProcesses = new List<ReportedProcess>();
        }
        
        internal static SandboxedProcessTraceBuilder CreateBuilderForTest() => new SandboxedProcessTraceBuilder();

        public SandboxedProcessOutput Freeze()
        {
            string file = m_fileStorage.GetFileName(SandboxedProcessFile.Trace);
            Encoding encoding = Encoding.UTF8;

            try
            {
                using FileStream stream = FileUtilities.CreateReplacementFile(
                    file,
                    FileShare.Read | FileShare.Delete,
                    openAsync: false);
                using StreamWriter writer = new StreamWriter(stream, encoding);

                WriteFile(writer);

                return new SandboxedProcessOutput(stream.Length, null, file, encoding, m_fileStorage, SandboxedProcessFile.Trace, null);
            }
            catch (Exception exp)
            {
                return new SandboxedProcessOutput(
                    SandboxedProcessOutput.NoLength,
                    value: null,
                    fileName: null,
                    encoding,
                    m_fileStorage,
                    SandboxedProcessFile.Trace,
                    new BuildXLException("An exception occurred while saving a trace file", innerException: exp));
            }
        }

        /// <summary>
        /// Report a single detours observation. The builder will decide whether it should be recorded or not.
        /// </summary>
        public void ReportFileAccess(
            uint processId,
            ReportedFileOperation operation,
            RequestedAccess requestedAccess,
            AbsolutePath manifestPath,
            string path,
            uint error,
            bool isAnAugmentedFileAccess,
            string enumeratePattern)
        {
            if (SkipOperation(operation))
            {
                return;
            }

            Operations.Add(new Operation
            {
                Id = m_fileAccessCounter++,
                ProcessId = processId,
                Error = error,
                ManifestPath = manifestPath,
                Path = GetPathId(path),
                FileOperation = operation,
                RequestedAccess = requestedAccess,
                IsAnAugmentedFileAccess = isAnAugmentedFileAccess,
                EnumeratePattern = enumeratePattern
            });
        }

        public void ReportProcess(ReportedProcess process)
        {
            m_reportedProcesses.Add(process);
        }

        private bool SkipOperation(ReportedFileOperation operation)
        {
            switch (operation)
            {
                case ReportedFileOperation.ChangedReadWriteToReadAccess:
                case ReportedFileOperation.FirstAllowWriteCheckInProcess:
                    return true;
                default:
                    return false;
            }
        }

        private int GetPathId(string path)
        {
            if (path == null)
            {
                return 0;
            }

            if (Paths.TryGetValue(path, out var pathId))
            {
                return pathId;
            }
            else
            {
                pathId = Paths.Count + 1;
                Paths.Add(path, pathId);
                return pathId;
            }
        }

        private void WriteFile(StreamWriter writer)
        {
            /*
                Schema:
                    Version number
                    ReportedFileOperation block
                        Count
                        (byte)ReportedFileOperation=ReportedFileOperation
                    RequestedAccess block
                        Count
                        (byte)RequestedAccess=RequestedAccess
                    Process block
                        Count
                        ProcessId = ReportedProcess
                    AbsolutePath block
                        Count
                        AbsolutePath.RawValue = AbsolutePath
                    Paths block
                        Count
                        m_paths[Path] = Path
                    Operations
                        Count
                        Operation.Id = Operation
             */

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

            foreach (var operation in Operations)
            {
                absolutePaths.Add(operation.ManifestPath);
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
#if NETCOREAPP3_0_OR_GREATER
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

            writer.WriteLine(Paths.Count);
            foreach (var kvp in Paths.OrderBy(kvp => kvp.Value))
            {
                writer.WriteLine($"{kvp.Value}={kvp.Key}");
            }

            writer.WriteLine(Operations.Count);
            foreach (var operation in Operations)
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
                // id, PID, ManifestPath, Path, FileOperation, RequestedAccess, Error, IsAnAugmentedFileAccess, EnumeratePattern
                sb.Append($"{operation.Id},{operation.ProcessId},");

                // if Path is not set, write ManifestPath
                if (operation.Path == 0)
                {
                    sb.Append($"{operation.ManifestPath.RawValue},,");
                }
                else
                {
                    sb.Append($",{operation.Path},");
                }

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
            public AbsolutePath ManifestPath { get; init; }
            public int Path { get; init; }
            public string EnumeratePattern { get; init; }
        }
    }
}
