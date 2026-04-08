// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Tracing;
using ZstdSharp;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeReplayXlgAnalyzer()
        {
            string outputFilePath = null;
            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else
                {
                    throw Error("Unknown option for replay XLG analysis: {0}", opt.Name);
                }
            }

            return new ReplayXlgAnalyzer(GetAnalysisInput())
            {
                OutputFilePath = outputFilePath,
            };
        }

        private static void WriteReplayXlgAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Replay XLG Analysis - Replay execution log to measure serialization size changes");
            writer.WriteOption("outputFile", "Optional. The file where to write the combined replayed execution log. If not specified, uses a temp file.", shortName: "o");
        }
    }

    /// <summary>
    /// Stream wrapper that counts bytes written while forwarding to an inner stream.
    /// </summary>
    internal sealed class CountingStream : Stream
    {
        private readonly Stream m_inner;
        public long BytesWritten { get; private set; }

        public CountingStream(Stream inner) => m_inner = inner;

        public override void Write(byte[] buffer, int offset, int count)
        {
            m_inner.Write(buffer, offset, count);
            BytesWritten += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            m_inner.Write(buffer);
            BytesWritten += buffer.Length;
        }

        public override void WriteByte(byte value)
        {
            m_inner.WriteByte(value);
            BytesWritten++;
        }

        public override void Flush() => m_inner.Flush();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Benchmarking analyzer that measures the XLG size impact of content hash interning.
    ///
    /// Content hash interning replaces repeated 32-byte content hashes with compact integer indices,
    /// significantly reducing XLG size for builds with millions of cache lookup events. This analyzer
    /// reads an existing XLG and replays all events into two compressed output files:
    ///   1. Baseline — no interning (original serialization format)
    ///   2. Interned — all interning enabled
    ///
    /// The output compares both uncompressed and compressed (zstd) sizes to quantify the savings.
    /// </summary>
    internal sealed class ReplayXlgAnalyzer : Analyzer
    {
        /// <summary>
        /// The path to the output file. If null, a temp file is used.
        /// </summary>
        public string OutputFilePath;

        private const int ZstdCompressionLevel = 2;

        // 2 output configurations: baseline (no interning) and interned (all interning)
        private BinaryLogger m_baselineLogger;
        private BinaryLogger m_internedLogger;

        private IExecutionLogTarget m_baselineTarget;
        private IExecutionLogTarget m_internedTarget;

        private string m_baselinePath;
        private string m_internedPath;

        private long m_eventCount;

        public ReplayXlgAnalyzer(AnalysisInput input)
            : base(input)
        {
        }

        // CountingStreams for uncompressed size measurement (sits between BinaryLogger and CompressionStream)
        private CountingStream m_baselineCounting;
        private CountingStream m_internedCounting;

        // File streams kept for compressed size measurement after loggers are disposed
        private FileStream m_baselineFileStream;
        private FileStream m_internedFileStream;

        private static BinaryLogger CreateCompressedLogger(
            string path,
            AnalysisInput input,
            out FileStream fileStream,
            out CountingStream countingStream)
        {
            fileStream = File.Open(path, FileMode.Create, FileAccess.ReadWrite);
            var compressedStream = new CompressionStream(fileStream, ZstdCompressionLevel, leaveOpen: true);
            countingStream = new CountingStream(compressedStream);
            return new BinaryLogger(
                countingStream,
                input.CachedGraph.Context,
                input.CachedGraph.PipGraph.GraphId,
                input.CachedGraph.PipGraph.MaxAbsolutePathIndex);
        }

        public override void Prepare()
        {
            string tempDir = Path.GetTempPath();
            m_baselinePath = Path.Combine(tempDir, "xlg_benchmark_baseline.xlg");
            m_internedPath = OutputFilePath ?? Path.Combine(tempDir, "xlg_benchmark_interned.xlg");

            m_baselineLogger = CreateCompressedLogger(m_baselinePath, Input, out m_baselineFileStream, out m_baselineCounting);
            m_baselineLogger.SuppressContentHashInterning = true;

            m_internedLogger = CreateCompressedLogger(m_internedPath, Input, out m_internedFileStream, out m_internedCounting);

            m_baselineTarget = new ExecutionLogFileTarget(m_baselineLogger, closeLogFileOnDispose: true);
            m_internedTarget = new ExecutionLogFileTarget(m_internedLogger, closeLogFileOnDispose: true);
        }

        protected override void ReportUnhandledEvent<TEventData>(TEventData data)
        {
            data.Metadata.LogToTarget(data, m_baselineTarget);
            data.Metadata.LogToTarget(data, m_internedTarget);
            m_eventCount++;
        }

        public override int Analyze()
        {
            m_baselineTarget.Dispose();
            m_internedTarget.Dispose();

            long compressedInputSize = new FileInfo(Input.ExecutionLogPath).Length;

            long baselineUncompressed = m_baselineCounting.BytesWritten;
            long internedUncompressed = m_internedCounting.BytesWritten;

            long baselineCompressed = m_baselineFileStream.Length;
            long internedCompressed = m_internedFileStream.Length;

            m_baselineFileStream.Dispose();
            m_internedFileStream.Dispose();

            Console.WriteLine();
            Console.WriteLine("=== XLG Size Comparison ===");
            Console.WriteLine($"  Events replayed:                  {m_eventCount:N0}");
            Console.WriteLine($"  Original XLG (on disk):            {FormatSize(compressedInputSize)}");
            Console.WriteLine();

            Console.WriteLine("  --- Uncompressed ---");
            PrintComparison("Baseline (no interning)", baselineUncompressed,
                            "Interned", internedUncompressed);

            Console.WriteLine("  --- Compressed (zstd level 2) ---");
            PrintComparison("Baseline (no interning)", baselineCompressed,
                            "Interned", internedCompressed);

            Console.WriteLine($"  Interning stats:");
            Console.WriteLine($"    Unique content hashes interned:  {m_internedLogger.UniqueContentHashCount:N0}");
            Console.WriteLine($"    Total hash references written:   {m_internedLogger.ContentHashEntriesWritten:N0}");
            if (m_internedLogger.ContentHashOverflowCount > 0)
            {
                Console.WriteLine($"    Overflow (inline after cap):     {m_internedLogger.ContentHashOverflowCount:N0}");
            }

            if (m_internedLogger.ContentHashEntriesWritten > 0 && m_internedLogger.UniqueContentHashCount > 0)
            {
                double avgRefsPerHash = (double)m_internedLogger.ContentHashEntriesWritten / m_internedLogger.UniqueContentHashCount;
                Console.WriteLine($"    Avg references per unique hash:  {avgRefsPerHash:F1}x");
            }

            // Clean up temp files (best-effort)
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
            try { File.Delete(m_baselinePath); } catch { }
            if (OutputFilePath == null)
            {
                try { File.Delete(m_internedPath); } catch { }
            }
#pragma warning restore ERP022
            else
            {
                Console.WriteLine($"  Output written to: {m_internedPath}");
            }

            return 0;
        }

        private static void PrintComparison(string baselineLabel, long baselineSize, string internedLabel, long internedSize)
        {
            long savings = baselineSize - internedSize;
            double pct = baselineSize > 0 ? (double)savings / baselineSize * 100.0 : 0;

            Console.WriteLine($"  {baselineLabel,-36} {FormatSize(baselineSize)}");
            Console.WriteLine($"  {internedLabel,-36} {FormatSize(internedSize)}");
            Console.WriteLine($"  {"Savings:",-36} {FormatSize(savings)} ({pct:F1}%)");
            Console.WriteLine();
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
            {
                return $"{bytes:N0} bytes ({bytes / (1024.0 * 1024 * 1024):F2} GB)";
            }

            return $"{bytes:N0} bytes ({bytes / (1024.0 * 1024):F2} MB)";
        }

        public override void Dispose()
        {
            m_baselineTarget?.Dispose();
            m_internedTarget?.Dispose();

            m_baselineFileStream?.Dispose();
            m_internedFileStream?.Dispose();
        }
    }
}
