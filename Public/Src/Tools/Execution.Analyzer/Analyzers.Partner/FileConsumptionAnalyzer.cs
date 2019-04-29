// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BuildXL.Pips;
using BuildXL.Pips.Artifacts;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.ParallelAlgorithms;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeFileConsumptionAnalyzer()
        {
            string outputFilePath = null;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("out", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else
                {
                    throw Error("Unknown option for fingerprint text analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrWhiteSpace(outputFilePath))
            {
                throw Error("Output file must be specified with /out");
            }

            return new FileConsumptionAnalyzer(GetAnalysisInput())
            {
                OutputFilePath = outputFilePath,
            };
        }
    }

    /// <summary>
    /// Placeholder analyzer for adding custom analyzer on demand for analyzing issues
    /// </summary>
    internal sealed class FileConsumptionAnalyzer : Analyzer
    {
        /// <summary>
        /// The path to the fingerprint file
        /// </summary>
        public string OutputFilePath;

        private StreamWriter m_writer;

        public long ProcessedPips = 0;

        private HashSet<PipId> m_producers = new HashSet<PipId>();

        private WorkerAnalyzer[] m_workers;

        public FileConsumptionAnalyzer(AnalysisInput input)
            : base(input)
        {
        }

        public override void Prepare()
        {
            m_writer = new StreamWriter(OutputFilePath);
        }

        public override bool CanHandleWorkerEvents => true;

        public override void WorkerList(WorkerListEventData data)
        {
            m_workers = data.Workers.SelectArray(s => new WorkerAnalyzer(this, s));
        }

        public override void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
        {
            GetWorkerAnalyzer().FileArtifactContentDecided(data);
        }

        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            if (data.Kind == FingerprintComputationKind.Execution)
            {
                if ((Interlocked.Increment(ref ProcessedPips) % 1000) == 0)
                {
                    Console.WriteLine($"Processing {ProcessedPips}");
                }

                GetWorkerAnalyzer().ProcessFingerprintComputed(data);
            }
        }

        private WorkerAnalyzer GetWorkerAnalyzer()
        {
            return m_workers[CurrentEventWorkerId];
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        public override int Analyze()
        {
            Console.WriteLine($"Analyzing");

            foreach (var worker in m_workers)
            {
                Console.WriteLine($"Completing {worker.Name}");
                worker.Complete();
            }

            m_writer.Dispose();
            return 0;
        }

        public override void Dispose()
        {
            m_writer.Dispose();
        }

        private enum ContentFlag
        {
            Deployed = 1,
            Static = 1 << 1,
            DynamicProbe = 1 << 2,
            DynamicContent = 1 << 3,
            Consumed = 1 << 4,
        }

        private class WorkerAnalyzer
        {
            private readonly FileConsumptionAnalyzer m_analyzer;

            private readonly ConcurrentBigMap<AbsolutePath, long> m_deployedFiles = new ConcurrentBigMap<AbsolutePath, long>();
            private readonly ConcurrentBigMap<AbsolutePath, ContentFlag> m_deployedFileFlags = new ConcurrentBigMap<AbsolutePath, ContentFlag>();

            private readonly ConcurrentBigMap<PathAtom, long> m_sizeByExtension = new ConcurrentBigMap<PathAtom, long>();

            private readonly ActionBlockSlim<ProcessFingerprintComputationEventData> m_processingBlock;

            public string Name { get; }

            public WorkerAnalyzer(FileConsumptionAnalyzer analyzer, string name)
            {
                m_analyzer = analyzer;
                Name = name;
                m_processingBlock = new ActionBlockSlim<ProcessFingerprintComputationEventData>(1, ProcessFingerprintComputedCore);
            }

            public void Complete()
            {
                m_processingBlock.Complete();
                var writer = m_analyzer.m_writer;
                writer.WriteLine($"Worker {Name}:");

                writer.WriteLine($"Total File Sizes by extension:");
                foreach (var entry in m_sizeByExtension.OrderByDescending(e => e.Value))
                {
                    writer.WriteLine($"{ToString(entry.Key)}={entry.Value}");
                }

                int counterMax = 5;
                var counters = Enumerable.Range(0, counterMax).Select(_ => new Counter()).ToArray();

                foreach (var entry in m_deployedFileFlags)
                {
                    for (int i = 0; i < counterMax; i++)
                    {
                        var flag = (ContentFlag)(1 << i);
                        if ((entry.Value & flag) == flag)
                        {
                            counters[i].Add(entry.Key, m_deployedFiles[entry.Key]);
                        }
                    }
                }

                for (int i = 0; i < counterMax; i++)
                {
                    var flag = (ContentFlag)(1 << i);
                    counters[i].Write(writer, flag.ToString());
                }

                writer.WriteLine();
            }

            private string ToString(PathAtom key)
            {
                if (!key.IsValid)
                {
                    return "<no extension>";
                }

                return key.ToString(m_analyzer.StringTable);
            }

            public void FileArtifactContentDecided(FileArtifactContentDecidedEventData data)
            {
                if (data.FileArtifact.IsOutputFile && data.FileContentInfo.HasKnownLength)
                {
                    if (m_deployedFiles.TryAdd(data.FileArtifact, data.FileContentInfo.Length))
                    {
                        m_sizeByExtension.AddOrUpdate(data.FileArtifact.Path.GetExtension(m_analyzer.PathTable), data.FileContentInfo.Length, (k, v) => v, (k, v, u) => v + u);
                    }

                    AddFlag(data.FileArtifact, ContentFlag.Deployed);
                }
            }

            public void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
            {
                m_processingBlock.Post(data);
            }

            private void AddFlag(AbsolutePath path, ContentFlag flag)
            {
                m_deployedFileFlags.AddOrUpdate(path, flag, (k, _flag) => _flag, (k, oldFlags, _flag) => oldFlags | flag);
            }

            public void ProcessFingerprintComputedCore(ProcessFingerprintComputationEventData data)
            {
                var computation = data.StrongFingerprintComputations[0];
                var pip = m_analyzer.GetPip(data.PipId);
                PipArtifacts.ForEachInput(pip, f =>
                {
                    if (f.IsFile && f.FileArtifact.IsOutputFile)
                    {
                        if (m_deployedFiles.TryGetValue(f.Path, out var size))
                        {
                            AddFlag(f.Path, ContentFlag.Static | ContentFlag.Consumed);
                        }
                    }

                    return true;
                }, includeLazyInputs: false);

                foreach (var input in computation.ObservedInputs)
                {
                    if (input.Type == ObservedInputType.FileContentRead || input.Type == ObservedInputType.ExistingFileProbe)
                    {
                        if (m_deployedFiles.TryGetValue(input.Path, out var size))
                        {
                            var flag = input.Type == ObservedInputType.FileContentRead ? ContentFlag.DynamicContent : ContentFlag.DynamicProbe;
                            AddFlag(input.Path, flag | ContentFlag.Consumed);
                        }
                    }
                }
            }

            public class Counter
            {
                public long FileCount = 0;
                public long TotalFileSize = 0;

                public void Add(AbsolutePath path, long fileSize)
                {
                    FileCount++;
                    TotalFileSize += fileSize;
                }

                public void Write(TextWriter writer, string prefix)
                {
                    writer.WriteLine($"    {prefix}: FileCount={FileCount}, TotalFileSize={TotalFileSize}");
                }
            }
        }
    }
}
