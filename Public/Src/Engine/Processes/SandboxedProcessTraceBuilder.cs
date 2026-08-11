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
    internal sealed class SandboxedProcessTraceBuilder : IDisposable
    {
        private const byte Version = 3;

        private readonly ISandboxedProcessFileStorage m_fileStorage;

        private readonly PathTable m_pathTable;

        /// <summary>
        /// Default number of operations whose serialized text is kept in memory before the
        /// per-operation blocks spill to temporary files. This bounds the in-memory operation text
        /// to a few MB no matter how file-access-heavy a pip is.
        /// </summary>
        internal const int DefaultMaxBufferedOperationsBeforeSpill = 50_000;

        // Distinct values needed to write the trace header, maintained incrementally so the
        // operation sequence never has to be scanned to build the header.
        private readonly HashSet<ReportedFileOperation> m_reportedFileOperations = [];

        private readonly HashSet<RequestedAccess> m_requestedAccesses = [];

        private readonly HashSet<AbsolutePath> m_absolutePaths = [];

        private readonly List<ReportedProcess> m_reportedProcesses = [];

        // The three per-operation trace blocks are serialized to text as each operation is reported
        // and appended here (in memory first, spilling to a temp file once the operation count
        // crosses the threshold). This avoids retaining any Operation objects on the write path;
        // only the header data above is kept in memory.
        private readonly OperationTextBlock m_operationsBlock;

        private readonly OperationTextBlock m_correlationBlock;

        private readonly OperationTextBlock m_attributesBlock;

        // Scratch builder reused to format each operation line. Reports are processed sequentially,
        // so a single reusable instance is safe and avoids per-operation allocations.
        private readonly StringBuilder m_lineBuilder = new();

        private uint m_fileAccessCounter;

        private int m_fileHasBeenSaved;

        /// <summary>
        /// Number of recorded operations.
        /// </summary>
        public int OperationCount => (int)m_fileAccessCounter;

        /// <summary>
        /// Number of reported processes.
        /// </summary>
        public int ReportedProcessCount => m_reportedProcesses.Count;

        /// <summary>
        /// Constructor.
        /// </summary>
        public SandboxedProcessTraceBuilder(ISandboxedProcessFileStorage fileStorage, PathTable pathTable)
            : this(fileStorage, pathTable, DefaultMaxBufferedOperationsBeforeSpill)
        {
        }

        /// <summary>
        /// Constructor allowing the in-memory spill threshold to be overridden (used by tests to
        /// exercise the spill path without recording a large number of operations).
        /// </summary>
        internal SandboxedProcessTraceBuilder(ISandboxedProcessFileStorage fileStorage, PathTable pathTable, int maxBufferedOperationsBeforeSpill)
        {
            Contract.Requires(!string.IsNullOrEmpty(fileStorage.GetFileName(SandboxedProcessFile.Trace)));
            Contract.Requires(pathTable != null);
            Contract.Requires(maxBufferedOperationsBeforeSpill > 0);

            m_fileStorage = fileStorage;
            m_pathTable = pathTable;

            string traceFile = fileStorage.GetFileName(SandboxedProcessFile.Trace);
            m_operationsBlock = new OperationTextBlock(traceFile + ".ops.tmp", maxBufferedOperationsBeforeSpill);
            m_correlationBlock = new OperationTextBlock(traceFile + ".corr.tmp", maxBufferedOperationsBeforeSpill);
            m_attributesBlock = new OperationTextBlock(traceFile + ".attr.tmp", maxBufferedOperationsBeforeSpill);
        }

        /// <summary>
        /// Freezes the trace and returns the output.
        /// </summary>
        public SandboxedProcessOutput Freeze()
        {
            string file = m_fileStorage.GetFileName(SandboxedProcessFile.Trace);
            Encoding encoding = Encoding.UTF8;
            SandboxedProcessOutput output;

            try
            {
                FileUtilities.CreateDirectory(Path.GetDirectoryName(file));
                using FileStream stream = FileUtilities.CreateReplacementFile(
                    file,
                    FileShare.Read | FileShare.Delete,
                    openAsync: false);
                using var writer = new StreamWriter(stream, encoding);

                WriteToStream(writer);

                output = new SandboxedProcessOutput(stream.Length, null, file, encoding, m_fileStorage, SandboxedProcessFile.Trace, null);
            }
            catch (Exception ex)
            {
                output = new SandboxedProcessOutput(
                    SandboxedProcessOutput.NoLength,
                    value: null,
                    fileName: null,
                    encoding,
                    m_fileStorage,
                    SandboxedProcessFile.Trace,
                    new BuildXLException("An exception occurred while saving a trace file", innerException: ex));
            }
            finally
            {
                // Ensure the temporary block files are closed and deleted even if writing failed,
                // including failures that occur before WriteToStream is reached.
                Dispose();
            }

            return output;
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
            string enumeratePattern,
            uint reportedFileAccessId,
            uint reportedFileAccessCorrelationId,
            FlagsAndAttributes flagsAndAttributes,
            FlagsAndAttributes openedFileOrDirectoryAttributes)
        {
            if (SkipOperation(operation))
            {
                return;
            }

            uint id = m_fileAccessCounter++;

            // Maintain the distinct-value sets incrementally so the trace header can be written
            // without scanning the operation sequence.
            m_reportedFileOperations.Add(operation);
            m_requestedAccesses.Add(requestedAccess);
            m_absolutePaths.Add(path);

            // Serialize the operation to text now (in the same format used when reading it back) and
            // append it to each of the three per-operation blocks, rather than retaining an Operation
            // object. The blocks buffer in memory and spill to temp files once the threshold is hit.

            // Operations block: id, PID, Path,, FileOperation, RequestedAccess, Error, IsAnAugmentedFileAccess, EnumeratePattern
            // Note the empty field between Path and FileOperation, kept to maintain the existing format.
            m_lineBuilder.Clear();
            m_lineBuilder.Append($"{id},{processId},");
            m_lineBuilder.Append($"{path.RawValue},,");
            m_lineBuilder.Append($"{(byte)operation},{(byte)requestedAccess},{error},{(isAnAugmentedFileAccess ? 1 : 0)},{enumeratePattern}");
            m_operationsBlock.AppendLine(m_lineBuilder);

            // Correlation block: id, reportedFileAccessId, reportedFileAccessCorrelationId
            m_lineBuilder.Clear();
            m_lineBuilder.Append($"{id},{reportedFileAccessId},{reportedFileAccessCorrelationId}");
            m_correlationBlock.AppendLine(m_lineBuilder);

            // Attributes block: id, FlagsAndAttributes, OpenedFileOrDirectoryAttributes
            m_lineBuilder.Clear();
            m_lineBuilder.Append($"{id},{(uint)flagsAndAttributes},{(uint)openedFileOrDirectoryAttributes}");
            m_attributesBlock.AppendLine(m_lineBuilder);
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
                operations.Add(new Operation(
                    Id: id,
                    ProcessId: processId,
                    Error: error,
                    FileOperation: fileOperation,
                    RequestedAccess: requestedAccess,
                    IsAnAugmentedFileAccess: isAnAugmentedFileAccess,
                    Path: path,
                    EnumeratePattern: enumeratePattern,
                    ReportedFileAccessId: 0,
                    ReportedFileAccessCorrelationId: 0,
                    FlagsAndAttributes: 0,
                    OpenedFileOrDirectoryAttributes: 0));
            }

            operationCount = int.Parse(reader.ReadLine());
            for (int i = 0; i < operationCount; i++)
            {
                // id, reportedFileAccessId, reportedFileAccessCorrelationId
                var parts = reader.ReadLine().Split(',');
                var reportedFileAccessId = uint.Parse(parts[1]);
                var reportedFileAccessCorrelationId = uint.Parse(parts[2]);
                operations[i] = operations[i] with
                {
                    ReportedFileAccessId = reportedFileAccessId,
                    ReportedFileAccessCorrelationId = reportedFileAccessCorrelationId
                };
            }

            operationCount = int.Parse(reader.ReadLine());
            for (int i = 0; i < operationCount; i++)
            {
                // id, FlagsAndAttributes, OpenedFileOrDirectoryAttributes
                var parts = reader.ReadLine().Split(',');
                var flagsAndAttributes = (FlagsAndAttributes)uint.Parse(parts[1]);
                var openedFileOrDirectoryAttributes = (FlagsAndAttributes)uint.Parse(parts[2]);
                operations[i] = operations[i] with
                {
                    FlagsAndAttributes = flagsAndAttributes,
                    OpenedFileOrDirectoryAttributes = openedFileOrDirectoryAttributes
                };
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
            //      Operations extra info (e.g., correlation)
            //          Count
            //          Operation.Id = Operation, Operation.ReportedFileAccessId, Operation.ReportedFileAccessCorrelationId

            Contract.Assert(Interlocked.CompareExchange(ref m_fileHasBeenSaved, 1, 0) == 0, "Trace file should be saved at most once.");

            try
            {
                WriteToStreamCore(writer);
            }
            finally
            {
                // Ensure the temporary block files are closed and deleted even if writing threw.
                Dispose();
            }
        }

        private void WriteToStreamCore(StreamWriter writer)
        {
            using var pooledSb = Pools.GetStringBuilder();
            var sb = pooledSb.Instance;

            writer.WriteLine(Version);

            writer.WriteLine(m_reportedFileOperations.Count);
            foreach (var fileOperation in m_reportedFileOperations)
            {
                writer.WriteLine($"{(byte)fileOperation}={fileOperation}");
            }

            writer.WriteLine(m_requestedAccesses.Count);
            foreach (var requestedAccess in m_requestedAccesses)
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

            writer.WriteLine(m_absolutePaths.Count);
            foreach (var absolutePath in m_absolutePaths.OrderBy(x => x.RawValue))
            {
                writer.WriteLine($"{absolutePath.RawValue}={absolutePath.ToString(m_pathTable)}");
            }

            // Paths block is deprecated
            // This count number is added to maintain compatibility with the existing format.
            writer.WriteLine(0);

            // The three per-operation blocks were serialized to text as operations were reported.
            // Emit each block's count followed by its (in-memory or spilled) contents verbatim.
            writer.WriteLine(OperationCount);
            m_operationsBlock.CopyTo(writer);

            writer.WriteLine(OperationCount);
            m_correlationBlock.CopyTo(writer);

            writer.WriteLine(OperationCount);
            m_attributesBlock.CopyTo(writer);

            static void formatReportedProcess(ReportedProcess process, StringBuilder sb)
            {
                // PID, "path", ParentPID, startTimeUtcTicks, endTimeUtcTicks
                // CommandLineArgs
                sb.Append($"{process.ProcessId},\"{process.Path}\",{process.ParentProcessId},");
                sb.AppendLine($"{process.CreationTime.ToUniversalTime().Ticks},{process.ExitTime.ToUniversalTime().Ticks},{process.ExitCode}");
                sb.Append(process.ProcessArgs);
            }
        }

        /// <summary>
        /// Closes and deletes the temporary block files (if any). Safe to call multiple times.
        /// </summary>
        public void Dispose()
        {
            m_operationsBlock?.Dispose();
            m_correlationBlock?.Dispose();
            m_attributesBlock?.Dispose();
        }

        /// <summary>
        /// Accumulates the serialized text of one per-operation trace block. Lines are buffered in
        /// memory until <see cref="Spill"/> is called, after which they (and every subsequent line)
        /// are written to a temporary file opened with DeleteOnClose. On <see cref="CopyTo"/> the
        /// accumulated text is written verbatim to the destination. This lets the builder serialize
        /// operations as they are reported without retaining <see cref="Operation"/> objects, while
        /// bounding memory for file-access-heavy pips.
        /// </summary>
        private sealed class OperationTextBlock : IDisposable
        {
            // Newline placed between operation lines. Matches the default StreamWriter.NewLine
            // (Environment.NewLine) used by every trace writer, so the output is byte-for-byte
            // identical to writing each line with StreamWriter.WriteLine.
            private static readonly string s_newLine = Environment.NewLine;

            private readonly string m_tempFilePath;

            private readonly int m_maxBufferedLinesBeforeSpill;

            private StringBuilder m_buffer = new();

            private int m_bufferedLineCount;

            private FileStream m_fileStream;

            private StreamWriter m_fileWriter;

            private bool m_spilled;

            public OperationTextBlock(string tempFilePath, int maxBufferedLinesBeforeSpill)
            {
                m_tempFilePath = tempFilePath;
                m_maxBufferedLinesBeforeSpill = maxBufferedLinesBeforeSpill;
            }

            /// <summary>
            /// Appends a single formatted line (without trailing newline) to the block, spilling the
            /// in-memory buffer to a temporary file once the configured threshold is reached.
            /// </summary>
            public void AppendLine(StringBuilder line)
            {
                if (m_spilled)
                {
                    WriteBuilder(m_fileWriter, line);
                    m_fileWriter.Write(s_newLine);
                    return;
                }

                AppendBuilder(m_buffer, line);
                m_buffer.Append(s_newLine);

                if (++m_bufferedLineCount >= m_maxBufferedLinesBeforeSpill)
                {
                    Spill();
                }
            }

            /// <summary>
            /// Moves the in-memory buffer to a temporary file and routes subsequent lines there.
            /// </summary>
            private void Spill()
            {
                if (m_spilled)
                {
                    return;
                }

                FileUtilities.CreateDirectory(Path.GetDirectoryName(m_tempFilePath));
                m_fileStream = new FileStream(
                    m_tempFilePath,
                    FileMode.Create,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 64 * 1024,
                    FileOptions.DeleteOnClose);

                // No BOM: this text is spliced into the middle of the trace stream.
                m_fileWriter = new StreamWriter(m_fileStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 64 * 1024, leaveOpen: true);

                WriteBuilder(m_fileWriter, m_buffer);
                m_buffer.Clear();
                m_buffer = null;
                m_spilled = true;
            }

            /// <summary>
            /// Writes the accumulated block text verbatim to the destination writer.
            /// </summary>
            public void CopyTo(StreamWriter writer)
            {
                if (m_spilled)
                {
                    // Flush both sides, then copy the temp file's raw UTF-8 bytes straight into the
                    // destination stream. Flushing the destination writer first keeps its buffered
                    // header/count bytes ahead of the copied block bytes.
                    m_fileWriter.Flush();
                    writer.Flush();
                    m_fileStream.Position = 0;
                    m_fileStream.CopyTo(writer.BaseStream);
                }
                else
                {
                    WriteBuilder(writer, m_buffer);
                }
            }

            public void Dispose()
            {
                m_fileWriter?.Dispose();
                m_fileWriter = null;

                // Disposing the stream closes the handle; DeleteOnClose then removes the temp file.
                m_fileStream?.Dispose();
                m_fileStream = null;
            }
        }

        private static void AppendBuilder(StringBuilder target, StringBuilder value)
        {
#if NETCOREAPP3_0_OR_GREATER
            target.Append(value);
#else
            target.Append(value.ToString());
#endif
        }

        private static void WriteBuilder(TextWriter target, StringBuilder value)
        {
#if NETCOREAPP3_0_OR_GREATER
            target.Write(value);
#else
            target.Write(value.ToString());
#endif
        }

        internal readonly record struct Operation(
            uint Id,
            uint ProcessId,
            uint Error,
            ReportedFileOperation FileOperation,
            RequestedAccess RequestedAccess,
            bool IsAnAugmentedFileAccess,
            AbsolutePath Path,
            string EnumeratePattern,
            uint ReportedFileAccessId,
            uint ReportedFileAccessCorrelationId,
            FlagsAndAttributes FlagsAndAttributes,
            FlagsAndAttributes OpenedFileOrDirectoryAttributes);
    }
}
