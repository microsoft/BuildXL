// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Scheduler.FileMonitoringViolationAnalyzer;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Tests for <see cref="FileMonitoringViolationAnalyzer"/> and <see cref="DependencyViolationEventSink"/> on a stubbed <see cref="IQueryablePipDependencyGraph"/>.
    /// </summary>
    public class FileMonitoringViolationAnalyzerTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        private readonly string ReportedExecutablePath = X("/X/bin/tool.exe");
        private readonly string JunkPath = X("/X/out/junk");
        private readonly string DoubleWritePath = X("/X/out/doublewrite");
        private readonly string ProducedPath = X("/X/out/produced");

        public FileMonitoringViolationAnalyzerTests(ITestOutputHelper output)
            : base(output) 
        {
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
        }

        [Fact]
        public void DoubleWriteNotReportedForPathOrderedAfter()
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new TestFileMonitoringViolationAnalyzer(LoggingContext, context, graph);

            AbsolutePath violatorOutput = CreateAbsolutePath(context, JunkPath);
            AbsolutePath producerOutput = CreateAbsolutePath(context, DoubleWritePath);

            Process violator = graph.AddProcess(violatorOutput);
            Process producer = graph.AddProcess(producerOutput);

            analyzer.AnalyzePipViolations(
                violator,
                new[] { CreateViolation(RequestedAccess.ReadWrite, producerOutput) },
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: null,
                allowedUndeclaredReads: null,
                absentPathProbesUnderOutputDirectories: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                out _);

            analyzer.AssertContainsViolation(
                new DependencyViolation(
                    FileMonitoringViolationAnalyzer.DependencyViolationType.UndeclaredOutput,
                    FileMonitoringViolationAnalyzer.AccessLevel.Write,
                    producerOutput,
                    violator,
                    null),
                "The violator has an undeclared output but it wasn't reported.");

            analyzer.AssertNoExtraViolationsCollected();
            AssertErrorEventLogged(EventId.FileMonitoringError);
        }

        [Fact]
        public void DoubleWriteReportedForPathOrderedBefore()
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new TestFileMonitoringViolationAnalyzer(LoggingContext, context, graph);

            AbsolutePath violatorOutput = CreateAbsolutePath(context, JunkPath);
            AbsolutePath producerOutput = CreateAbsolutePath(context, DoubleWritePath);

            Process producer = graph.AddProcess(producerOutput);
            Process violator = graph.AddProcess(violatorOutput);

            analyzer.AnalyzePipViolations(
                violator,
                new[] { CreateViolation(RequestedAccess.ReadWrite, producerOutput) },
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: null,
                allowedUndeclaredReads: null,
                absentPathProbesUnderOutputDirectories: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                out _);

            analyzer.AssertContainsViolation(
                new DependencyViolation(
                    FileMonitoringViolationAnalyzer.DependencyViolationType.DoubleWrite,
                    FileMonitoringViolationAnalyzer.AccessLevel.Write,
                    producerOutput,
                    violator,
                    producer),
                "The violator is after the producer, so this should be a double-write on the produced path.");
            analyzer.AssertNoExtraViolationsCollected();
            AssertErrorEventLogged(EventId.FileMonitoringError);
        }

        [Fact]
        public void DoubleWriteEventLoggedOnViolationError()
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new FileMonitoringViolationAnalyzer(LoggingContext, context, graph, new QueryableEmptyFileContentManager(), validateDistribution: false, ignoreDynamicWritesOnAbsentProbes: false, unexpectedFileAccessesAsErrors: true);

            AbsolutePath violatorOutput = CreateAbsolutePath(context, JunkPath);
            AbsolutePath producerOutput = CreateAbsolutePath(context, DoubleWritePath);

            Process producer = graph.AddProcess(producerOutput);
            Process violator = graph.AddProcess(violatorOutput);

            analyzer.AnalyzePipViolations(
                violator,
                new[] { CreateViolation(RequestedAccess.ReadWrite, producerOutput) },
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: null,
                allowedUndeclaredReads: null,
                absentPathProbesUnderOutputDirectories: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                out _);

            AssertVerboseEventLogged(LogEventId.DependencyViolationDoubleWrite);
            AssertErrorEventLogged(EventId.FileMonitoringError);
        }

        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 2, MemberType = typeof(TruthTable))]
        public void DoubleWriteEventLoggedOnViolation(bool validateDistribution, bool unexpectedFileAccessesAsErrors)
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new FileMonitoringViolationAnalyzer(
                LoggingContext, 
                context, 
                graph,
                new QueryableEmptyFileContentManager(), 
                validateDistribution: validateDistribution, 
                unexpectedFileAccessesAsErrors: unexpectedFileAccessesAsErrors,
                ignoreDynamicWritesOnAbsentProbes: false);

            AbsolutePath violatorOutput = CreateAbsolutePath(context, JunkPath);
            AbsolutePath producerOutput = CreateAbsolutePath(context, DoubleWritePath);

            Process producer = graph.AddProcess(producerOutput);
            Process violator = graph.AddProcess(violatorOutput);

            analyzer.AnalyzePipViolations(
                violator,
                new[] { CreateViolation(RequestedAccess.ReadWrite, producerOutput) },
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: null,
                allowedUndeclaredReads: null,
                absentPathProbesUnderOutputDirectories: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                out _);

            AssertVerboseEventLogged(LogEventId.DependencyViolationDoubleWrite);

            if (unexpectedFileAccessesAsErrors)
            {
                AssertErrorEventLogged(EventId.FileMonitoringError);
            }
            else
            {
                AssertWarningEventLogged(EventId.FileMonitoringWarning);
            }
        }

        [Fact]
        public void ReadLevelViolationReportedForUnknownPath()
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new TestFileMonitoringViolationAnalyzer(LoggingContext, context, graph);

            AbsolutePath unknown = CreateAbsolutePath(context, X("/X/out/unknown"));
            AbsolutePath produced = CreateAbsolutePath(context, ProducedPath);

            Process process = graph.AddProcess(produced);

            analyzer.AnalyzePipViolations(
                process,
                new[] { CreateViolation(RequestedAccess.Read, unknown) },
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: null,
                allowedUndeclaredReads: null,
                absentPathProbesUnderOutputDirectories: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                out _);

            analyzer.AssertContainsViolation(
                new DependencyViolation(
                    FileMonitoringViolationAnalyzer.DependencyViolationType.MissingSourceDependency,
                    FileMonitoringViolationAnalyzer.AccessLevel.Read,
                    unknown,
                    process,
                    null),
                "A MissingSourceDependency should have been reported with no suggested value");

            analyzer.AssertNoExtraViolationsCollected();
            AssertErrorEventLogged(EventId.FileMonitoringError);
        }

        [Fact]
        public void UndeclaredReadCycleReportedForPathOrderedAfter()
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new TestFileMonitoringViolationAnalyzer(LoggingContext, context, graph);

            AbsolutePath violatorOutput = CreateAbsolutePath(context, JunkPath);
            AbsolutePath producerOutput = CreateAbsolutePath(context, ProducedPath);

            Process violator = graph.AddProcess(violatorOutput);
            Process producer = graph.AddProcess(producerOutput);

            analyzer.AnalyzePipViolations(
                violator,
                new[] { CreateViolation(RequestedAccess.Read, producerOutput) },
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: null,
                allowedUndeclaredReads: null,
                absentPathProbesUnderOutputDirectories: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                out _);

            analyzer.AssertContainsViolation(
                new DependencyViolation(
                    FileMonitoringViolationAnalyzer.DependencyViolationType.UndeclaredReadCycle,
                    FileMonitoringViolationAnalyzer.AccessLevel.Read, 
                    producerOutput,
                    violator,
                    producer),
                "An UndeclaredReadCycle should have been reported since an earlier pip has an undeclared read of a later pip's output.");

            analyzer.AssertNoExtraViolationsCollected();
            AssertErrorEventLogged(EventId.FileMonitoringError);
        }

        [Fact]
        public void ReadRaceReportedForConcurrentPath()
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new TestFileMonitoringViolationAnalyzer(LoggingContext, context, graph);

            AbsolutePath violatorOutput = CreateAbsolutePath(context, JunkPath);
            AbsolutePath producerOutput = CreateAbsolutePath(context, ProducedPath);

            Process producer = graph.AddProcess(producerOutput);
            Process violator = graph.AddProcess(violatorOutput);
            graph.SetConcurrentRange(producer, violator);

            analyzer.AnalyzePipViolations(
                violator,
                new[] { CreateViolation(RequestedAccess.Read, producerOutput) },
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: null,
                allowedUndeclaredReads: null,
                absentPathProbesUnderOutputDirectories: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                out _);

            analyzer.AssertContainsViolation(
                new DependencyViolation(
                    FileMonitoringViolationAnalyzer.DependencyViolationType.ReadRace,
                    FileMonitoringViolationAnalyzer.AccessLevel.Read,
                    producerOutput,
                    violator,
                    producer),
                "The violator is concurrent with the producer, so this should be a read-race on the produced path.");
            analyzer.AssertNoExtraViolationsCollected();
            AssertErrorEventLogged(EventId.FileMonitoringError);
        }

        [Fact]
        public void ReadRaceEventLoggedOnViolation()
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new FileMonitoringViolationAnalyzer(LoggingContext, context, graph, new QueryableEmptyFileContentManager(), validateDistribution: false, ignoreDynamicWritesOnAbsentProbes: false, unexpectedFileAccessesAsErrors: true);

            AbsolutePath violatorOutput = CreateAbsolutePath(context, JunkPath);
            AbsolutePath producerOutput = CreateAbsolutePath(context, ProducedPath);

            Process producer = graph.AddProcess(producerOutput);
            Process violator = graph.AddProcess(violatorOutput);
            graph.SetConcurrentRange(producer, violator);

            analyzer.AnalyzePipViolations(
                violator,
                new[] { CreateViolation(RequestedAccess.Read, producerOutput) },
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: null,
                allowedUndeclaredReads: null,
                absentPathProbesUnderOutputDirectories: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                out _);
            AssertVerboseEventLogged(LogEventId.DependencyViolationReadRace);
            AssertErrorEventLogged(EventId.FileMonitoringError);
        }

        [Fact]
        public void ReadRaceEventLoggedOnViolationDistributed()
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new FileMonitoringViolationAnalyzer(LoggingContext, context, graph, new QueryableEmptyFileContentManager(), validateDistribution: true, ignoreDynamicWritesOnAbsentProbes: false, unexpectedFileAccessesAsErrors: true);

            AbsolutePath violatorOutput = CreateAbsolutePath(context, JunkPath);
            AbsolutePath producerOutput = CreateAbsolutePath(context, ProducedPath);

            Process producer = graph.AddProcess(producerOutput);
            Process violator = graph.AddProcess(violatorOutput);
            graph.SetConcurrentRange(producer, violator);

            AnalyzePipViolationsResult analyzePipViolationsResult = 
                analyzer.AnalyzePipViolations(
                    violator,
                    new[] { CreateViolation(RequestedAccess.Read, producerOutput) },
                    null, // whitelisted accesses
                    exclusiveOpaqueDirectoryContent: null,
                    sharedOpaqueDirectoryWriteAccesses: null,
                    allowedUndeclaredReads: null,
                    absentPathProbesUnderOutputDirectories: null,
                    ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                    out _); 

            AssertTrue(!analyzePipViolationsResult.IsViolationClean);
            AssertErrorEventLogged(EventId.FileMonitoringError);
            AssertVerboseEventLogged(LogEventId.DependencyViolationReadRace);
        }

        [Fact]
        public void UndeclaredOrderedReadReportedForPathOrderedBefore()
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new TestFileMonitoringViolationAnalyzer(LoggingContext, context, graph, true);

            AbsolutePath producerOutput = CreateAbsolutePath(context, ProducedPath);
            AbsolutePath violatorOutput = CreateAbsolutePath(context, JunkPath);

            Process producer = graph.AddProcess(producerOutput);
            Process violator = graph.AddProcess(violatorOutput);

            analyzer.AnalyzePipViolations(
                violator,
                new[] { CreateViolation(RequestedAccess.Read, producerOutput) },
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: null,
                allowedUndeclaredReads: null,
                absentPathProbesUnderOutputDirectories: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                out _);

            AssertVerboseEventLogged(LogEventId.DependencyViolationUndeclaredOrderedRead);
            AssertErrorEventLogged(EventId.FileMonitoringError);
        }

        [Fact]
        public void MultipleViolationsReportedForSinglePip()
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new TestFileMonitoringViolationAnalyzer(LoggingContext, context, graph);

            AbsolutePath produced1 = CreateAbsolutePath(context, X("/X/out/produced1"));
            AbsolutePath produced2 = CreateAbsolutePath(context, X("/X/out/produced2"));
            AbsolutePath produced3 = CreateAbsolutePath(context, X("/X/out/produced3"));

            Process producer1 = graph.AddProcess(produced1);
            Process producer2 = graph.AddProcess(produced2);
            Process producer3 = graph.AddProcess(produced3);
            Process violator = graph.AddProcess(CreateAbsolutePath(context, X("/X/out/violator")));

            graph.SetConcurrentRange(producer3, violator);

            analyzer.AnalyzePipViolations(
                violator,
                new[]
                {
                    CreateViolation(RequestedAccess.Read, produced3),
                    CreateViolation(RequestedAccess.Read, produced2),
                    CreateViolation(RequestedAccess.Write, produced1),
                },
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: null,
                allowedUndeclaredReads: null,
                absentPathProbesUnderOutputDirectories: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                out _);

            analyzer.AssertContainsViolation(
                new DependencyViolation(
                    FileMonitoringViolationAnalyzer.DependencyViolationType.ReadRace,
                    FileMonitoringViolationAnalyzer.AccessLevel.Read,
                    produced3,
                    violator,
                    producer3),
                "The violator is concurrent with the producer, so this should be a read-race on the produced path.");

            analyzer.AssertContainsViolation(
                new DependencyViolation(
                    FileMonitoringViolationAnalyzer.DependencyViolationType.UndeclaredOrderedRead,
                    FileMonitoringViolationAnalyzer.AccessLevel.Read,
                    produced2,
                    violator,
                    producer2),
                "The violator has an undeclared read on the producer but it wasn't reported.");

            analyzer.AssertContainsViolation(
                new DependencyViolation(
                    FileMonitoringViolationAnalyzer.DependencyViolationType.DoubleWrite,
                    FileMonitoringViolationAnalyzer.AccessLevel.Write,
                    produced1,
                    violator,
                    producer1),
                "The violator is after the producer, so this should be a double-write on the produced path.");

            analyzer.AssertNoExtraViolationsCollected();
            AssertErrorEventLogged(EventId.FileMonitoringError);
        }

        [Fact]
        public void UndeclaredOutput()
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new TestFileMonitoringViolationAnalyzer(LoggingContext, context, graph);

            AbsolutePath declaredOutput = CreateAbsolutePath(context, X("/X/out/produced1"));
            AbsolutePath undeclaredOutput = CreateAbsolutePath(context, X("/X/out/produced2"));
            Process producer = graph.AddProcess(declaredOutput);

            analyzer.AnalyzePipViolations(
                producer,
                new[]
                {
                    CreateViolation(RequestedAccess.Write, undeclaredOutput),
                },
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: null,
                allowedUndeclaredReads: null,
                absentPathProbesUnderOutputDirectories: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                out _);

            analyzer.AssertContainsViolation(
                new DependencyViolation(
                    FileMonitoringViolationAnalyzer.DependencyViolationType.UndeclaredOutput,
                    FileMonitoringViolationAnalyzer.AccessLevel.Write,
                    undeclaredOutput,
                    producer,
                    null),
                "The violator has an undeclared output but it wasn't reported.");
            AssertErrorEventLogged(EventId.FileMonitoringError);
        }

        [Fact]
        public void ReadUndeclaredOutput()
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new TestFileMonitoringViolationAnalyzer(LoggingContext, context, graph, true);

            AbsolutePath producerOutput = CreateAbsolutePath(context, ProducedPath);
            AbsolutePath undeclaredOutput = CreateAbsolutePath(context, X("/X/out/produced2"));
            AbsolutePath consumerOutput = CreateAbsolutePath(context, JunkPath);
            AbsolutePath consumerOutput2 = CreateAbsolutePath(context, X("/X/out/junk2"));

            Process producerViolator = graph.AddProcess(producerOutput);
            Process consumerViolator = graph.AddProcess(consumerOutput);
            Process consumerViolator2 = graph.AddProcess(consumerOutput2);

            analyzer.AnalyzePipViolations(
                consumerViolator,
                new[]
                {
                    CreateViolation(RequestedAccess.Read, undeclaredOutput),
                },
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: null,
                allowedUndeclaredReads: null,
                absentPathProbesUnderOutputDirectories: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                out _);

            analyzer.AnalyzePipViolations(
                producerViolator,
                new[]
                {
                    CreateViolation(RequestedAccess.Write, undeclaredOutput),
                },
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: null,
                allowedUndeclaredReads: null,
                absentPathProbesUnderOutputDirectories: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                out _);

            analyzer.AnalyzePipViolations(
                consumerViolator2,
                new[] 
                { 
                    CreateViolation(RequestedAccess.Read, undeclaredOutput) 
                },
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: null,
                allowedUndeclaredReads: null,
                absentPathProbesUnderOutputDirectories: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                out _);

            analyzer.AssertContainsViolation(
                new DependencyViolation(
                    FileMonitoringViolationAnalyzer.DependencyViolationType.UndeclaredOutput,
                    FileMonitoringViolationAnalyzer.AccessLevel.Write,
                    undeclaredOutput,
                    producerViolator,
                    null),
                    "The violator has wrote undeclared output but it wasn't reported.");

            analyzer.AssertContainsViolation(
                new DependencyViolation(
                    FileMonitoringViolationAnalyzer.DependencyViolationType.ReadUndeclaredOutput,
                    FileMonitoringViolationAnalyzer.AccessLevel.Read,
                    undeclaredOutput,
                    consumerViolator2,
                    producerViolator),
                    "The violator read an undeclared output but it wasn't reported as ReadUndeclaredOutput.");

            analyzer.AssertContainsViolation(
                new DependencyViolation(
                    FileMonitoringViolationAnalyzer.DependencyViolationType.MissingSourceDependency,
                    FileMonitoringViolationAnalyzer.AccessLevel.Read,
                    undeclaredOutput,
                    consumerViolator2,
                    null),
                    "The violator read an undeclared output but it wasn't reported as MissingSourceDependency.");

            analyzer.AssertContainsViolation(
                new DependencyViolation(
                    FileMonitoringViolationAnalyzer.DependencyViolationType.ReadUndeclaredOutput,
                    FileMonitoringViolationAnalyzer.AccessLevel.Read,
                    undeclaredOutput,
                    consumerViolator,
                    producerViolator),
                    "The violator read an undeclared output but it wasn't reported as ReadUndeclaredOutput.");

            analyzer.AssertContainsViolation(
                new DependencyViolation(
                    FileMonitoringViolationAnalyzer.DependencyViolationType.MissingSourceDependency,
                    FileMonitoringViolationAnalyzer.AccessLevel.Read,
                    undeclaredOutput,
                    consumerViolator,
                    null),
                    "The violator read an undeclared output but it wasn't reported as MissingSourceDependency.");

            AssertVerboseEventLogged(LogEventId.DependencyViolationReadUndeclaredOutput, 2);
            AssertVerboseEventLogged(LogEventId.DependencyViolationUndeclaredOutput);
            AssertVerboseEventLogged(LogEventId.DependencyViolationMissingSourceDependency, 2);
            AssertErrorEventLogged(EventId.FileMonitoringError, 3);
        }

        [Fact]
        public void AggregateLogMessageOnUndeclaredReadWrite()
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new TestFileMonitoringViolationAnalyzer(LoggingContext, context, graph);

            AbsolutePath declaredOuput = CreateAbsolutePath(context, X("/X/out/declared"));
            AbsolutePath undeclaredRead = CreateAbsolutePath(context, X("/X/out/undeclaredR"));
            AbsolutePath undeclaredWrite = CreateAbsolutePath(context, X("/X/out/undeclaredW"));
            AbsolutePath undeclaredReadWrite = CreateAbsolutePath(context, X("/X/out/undeclaredRW"));

            Process process = graph.AddProcess(declaredOuput);

            analyzer.AnalyzePipViolations(
                process,
                new[]
                {
                    CreateViolation(RequestedAccess.Read, undeclaredRead),
                    CreateViolation(RequestedAccess.Write, undeclaredWrite),
                    CreateViolation(RequestedAccess.Read, undeclaredReadWrite),
                    CreateViolation(RequestedAccess.Write, undeclaredReadWrite)
                },
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: null,
                allowedUndeclaredReads: null,
                absentPathProbesUnderOutputDirectories: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                out _);

            AssertErrorEventLogged(EventId.FileMonitoringError);

            // The ending of the expected log message.
            // The order of files here is based on the sorting in FileMonitoringViolationAnalyzer.AggregateAccessViolationPaths()
            StringBuilder expectedLog = new StringBuilder();
            expectedLog.AppendLine("- Disallowed file accesses were detected (R = read, W = write):");
            // process that 'caused' the violations
            expectedLog.AppendLine($"Disallowed file accesses performed by: {ReportedExecutablePath}");
            // aggregated paths
            expectedLog.AppendLine(ReportedFileAccess.ReadDescriptionPrefix + undeclaredRead.ToString(context.PathTable));
            expectedLog.AppendLine(ReportedFileAccess.WriteDescriptionPrefix + undeclaredReadWrite.ToString(context.PathTable));
            expectedLog.AppendLine(ReportedFileAccess.WriteDescriptionPrefix + undeclaredWrite.ToString(context.PathTable));

            var eventLog = OperatingSystemHelper.IsUnixOS ? EventListener.GetLog().Replace("\r", "") : EventListener.GetLog();
            XAssert.AreEqual(1, CountInstancesOfWord(expectedLog.ToString(), eventLog), eventLog);
        }

        [Fact]
        public void AllViolationTypesHandledByReporting()
        {
            var testContext = BuildXLContext.CreateInstanceForTesting();
            var tool = AbsolutePath.Create(testContext.PathTable, X("/X/out/tool.exe"));
            var path = AbsolutePath.Create(testContext.PathTable, X("/X/out/path.h"));

            foreach (DependencyViolationType violationType in Enum.GetValues(typeof(DependencyViolationType)))
            {
                ReportedViolation violation = new ReportedViolation(isError: false, type: violationType, path: path, violatorPipId: new PipId(1), PipId.Invalid, tool);

                // Should not throw
                Analysis.IgnoreResult(violation.ReportingType);
                Analysis.IgnoreResult(violation.RenderForDFASummary(testContext.PathTable));
                Analysis.IgnoreResult(violation.LegendText);
            }
        }

        [Fact]
        public void ReportedViolationEqualityTest()
        {
            ReportedViolation violation = new ReportedViolation(isError: false, type: DependencyViolationType.DoubleWrite, path: AbsolutePath.Invalid, violatorPipId: PipId.Invalid, relatedPipId: PipId.Invalid, processPath: AbsolutePath.Invalid);
            ReportedViolation violation2 = new ReportedViolation(isError: false, type: DependencyViolationType.DoubleWrite, path: AbsolutePath.Invalid, violatorPipId: PipId.Invalid, relatedPipId: null, processPath: AbsolutePath.Invalid);
            Assert.NotEqual(violation, violation2);
        }

        /// <summary>
        /// Makes sure that when multiple file operations happen on the same path the appropriate precidence is chosen.
        /// </summary>
        [Theory]
        [InlineData("DW", new DependencyViolationType[] { DependencyViolationType.DoubleWrite, DependencyViolationType.ReadRace })]
        [InlineData("DW", new DependencyViolationType[] { DependencyViolationType.ReadRace, DependencyViolationType.DoubleWrite })]
        [InlineData("DW", new DependencyViolationType[] { DependencyViolationType.DoubleWrite })]
        [InlineData("R", new DependencyViolationType[] { DependencyViolationType.ReadRace, DependencyViolationType.UndeclaredOrderedRead })]
        [InlineData("R", new DependencyViolationType[] { DependencyViolationType.ReadRace, DependencyViolationType.AbsentPathProbeUnderUndeclaredOpaque })]
        [InlineData("W", new DependencyViolationType[] { DependencyViolationType.ReadRace, DependencyViolationType.WriteInExistingFile })]
        public void AnalyzerMessageRenderingOperationPrecedence(string expectedPrefix, DependencyViolationType[] violationTypes)
        {
            var testContext = BuildXLContext.CreateInstanceForTesting();
            var tool = AbsolutePath.Create(testContext.PathTable, X("/X/out/tool.exe"));
            var path = AbsolutePath.Create(testContext.PathTable, X("/X/out/path.h"));

            HashSet<ReportedViolation> violations = new HashSet<ReportedViolation>();
            foreach (DependencyViolationType violationType in violationTypes)
            {
                violations.Add(new ReportedViolation(isError: false, type: violationType, path: path, violatorPipId: new PipId(1), PipId.Invalid, tool));
            }

            string result = FileMonitoringViolationAnalyzer.AggregateAccessViolationPaths(violations, testContext.PathTable, (pipId) => "dummy");
            string accessLine = result.Split(new string [] { Environment.NewLine}, StringSplitOptions.None)[1].TrimStart(' ');

            XAssert.IsTrue(accessLine.StartsWith(expectedPrefix), "Unexpected result: " + result);
        }

        [Fact]
        public void AnalyzerMessageRenderingLegendInformation()
        {
            var testContext = BuildXLContext.CreateInstanceForTesting();
            var tool1 = AbsolutePath.Create(testContext.PathTable, X("/X/out/tool1.exe"));
            var tool2 = AbsolutePath.Create(testContext.PathTable, X("/X/out/tool2.exe"));
            var pathA = AbsolutePath.Create(testContext.PathTable, X("/X/out/pathA.h"));
            var pathB = AbsolutePath.Create(testContext.PathTable, X("/X/out/pathB.h"));

            HashSet<ReportedViolation> violations = new HashSet<ReportedViolation>();
            violations.Add(new ReportedViolation(isError: false, type: DependencyViolationType.DoubleWrite, path: pathA,
                violatorPipId: new PipId(1), relatedPipId: PipId.Invalid, tool1));
            violations.Add(new ReportedViolation(isError: false, type: DependencyViolationType.AbsentPathProbeUnderUndeclaredOpaque, path: pathB,
                violatorPipId: new PipId(1), relatedPipId: new PipId(2), tool1));
            violations.Add(new ReportedViolation(isError: false, type: DependencyViolationType.WriteInExistingFile, path: pathB,
                violatorPipId: new PipId(1), relatedPipId: PipId.Invalid, tool2));
            violations.Add(new ReportedViolation(isError: false, type: DependencyViolationType.WriteInExistingFile, path: pathB,
                violatorPipId: new PipId(1), relatedPipId: PipId.Invalid, tool2));

            string result = FileMonitoringViolationAnalyzer.AggregateAccessViolationPaths(violations, testContext.PathTable, (pipId) => "PLACEHOLDER PIP DESCRIPTION");

            // Should see a single instance of " W " for the write files legend even though there are 2 write violations
            string legendMarker = " = ";
            XAssert.AreEqual(1, CountInstancesOfWord(SimplifiedViolationType.Write.ToAbbreviation() + legendMarker, result), result);

            XAssert.AreEqual(1, CountInstancesOfWord(SimplifiedViolationType.DoubleWrite.ToAbbreviation() + legendMarker, result), result);
            XAssert.AreEqual(1, CountInstancesOfWord(SimplifiedViolationType.Probe.ToAbbreviation() + legendMarker, result), result);
        }

        private int CountInstancesOfWord(string word, string stringToSearch)
        {
            int originalLength = stringToSearch.Length;
            int newLength = stringToSearch.Replace(word, "").Length;

            return (originalLength - newLength) / word.Length;
        }

        [Theory]
        [InlineData(DoubleWritePolicy.DoubleWritesAreErrors)]
        [InlineData(DoubleWritePolicy.UnsafeFirstDoubleWriteWins)]
        public void DoubleWritePolicyDeterminesViolationSeverity(DoubleWritePolicy doubleWritePolicy)
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new TestFileMonitoringViolationAnalyzer(
                LoggingContext, 
                context, 
                graph,
                // Set this to test the logic of base.HandleDependencyViolation(...) instead of the overriding fake
                doLogging: true,
                collectNonErrorViolations: true);

            AbsolutePath violatorOutput = CreateAbsolutePath(context, JunkPath);
            AbsolutePath producerOutput = CreateAbsolutePath(context, DoubleWritePath);

            Process producer = graph.AddProcess(producerOutput, doubleWritePolicy);
            Process violator = graph.AddProcess(violatorOutput, doubleWritePolicy);

            analyzer.AnalyzePipViolations(
                violator,
                new[] { CreateViolation(RequestedAccess.ReadWrite, producerOutput) },
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: null,
                allowedUndeclaredReads: null,
                absentPathProbesUnderOutputDirectories: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                out _);

            analyzer.AssertContainsViolation(
                new DependencyViolation(
                    FileMonitoringViolationAnalyzer.DependencyViolationType.DoubleWrite,
                    FileMonitoringViolationAnalyzer.AccessLevel.Write,
                    producerOutput,
                    violator,
                    producer),
                "The violator is after the producer, so this should be a double-write on the produced path.");

            analyzer.AssertNoExtraViolationsCollected();
            AssertVerboseEventLogged(LogEventId.DependencyViolationDoubleWrite);

            // Based on the double write policy, the violation is an error or a warning
            if (doubleWritePolicy == DoubleWritePolicy.DoubleWritesAreErrors)
            {
                AssertErrorEventLogged(EventId.FileMonitoringError);
            }
            else
            {
                AssertWarningEventLogged(EventId.FileMonitoringWarning);
            }
        }

        [Theory]
        [InlineData(DoubleWritePolicy.DoubleWritesAreErrors)]
        [InlineData(DoubleWritePolicy.AllowSameContentDoubleWrites)]
        public void DoubleWritePolicyIsContentAware(DoubleWritePolicy doubleWritePolicy)
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new TestFileMonitoringViolationAnalyzer(
                LoggingContext,
                context,
                graph,
                // Set this to test the logic of base.HandleDependencyViolation(...) instead of the overriding fake
                doLogging: true,
                collectNonErrorViolations: true);

            // Create the path where the double write will occur, and a random file content that will be used for both producers
            AbsolutePath doubleWriteOutput = CreateAbsolutePath(context, JunkPath);
            ContentHash contentHash = ContentHashingUtilities.CreateRandom();
            var fileContentInfo = new FileContentInfo(contentHash, contentHash.Length);
            var outputsContent = new (FileArtifact, FileMaterializationInfo, PipOutputOrigin)[]
                {
                    (FileArtifact.CreateOutputFile(doubleWriteOutput), new FileMaterializationInfo(fileContentInfo, doubleWriteOutput.GetName(context.PathTable)), PipOutputOrigin.NotMaterialized)
                }.ToReadOnlyArray();
            var sharedOpaqueRoot = doubleWriteOutput.GetParent(context.PathTable);
            var sharedOpaqueDirectoryWriteAccesses = new Dictionary<AbsolutePath, IReadOnlyCollection<AbsolutePath>> { [sharedOpaqueRoot] = new AbsolutePath[] { doubleWriteOutput } };

            // Create two processes that claim to produce some arbitrary static output files. We are not really use those outputs but tell
            // the analyzer that these processes wrote into shared opaques
            Process producer = graph.AddProcess(CreateAbsolutePath(context, X("/X/out/static1")), doubleWritePolicy);
            Process violator = graph.AddProcess(CreateAbsolutePath(context, X("/X/out/static2")), doubleWritePolicy);

            // Run the analysis for both pips
            analyzer.AnalyzePipViolations(
                producer,
                new ReportedFileAccess[0],
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: sharedOpaqueDirectoryWriteAccesses,
                allowedUndeclaredReads: null,
                absentPathProbesUnderOutputDirectories: null,
                outputsContent,
                out _);

            analyzer.AnalyzePipViolations(
                violator,
                new ReportedFileAccess[0],
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: sharedOpaqueDirectoryWriteAccesses,
                allowedUndeclaredReads: null,
                absentPathProbesUnderOutputDirectories: null,
                outputsContent,
                out _);

            // Based on the double write policy, the violation is an error or it is not raised
            if (doubleWritePolicy == DoubleWritePolicy.DoubleWritesAreErrors)
            {
                AssertErrorEventLogged(EventId.FileMonitoringError);
                AssertVerboseEventLogged(LogEventId.DependencyViolationDoubleWrite);
            }
            else
            {
                // AllowSameContentDoubleWrites case
                AssertVerboseEventLogged(LogEventId.AllowedSameContentDoubleWrite);
                analyzer.AssertNoExtraViolationsCollected();
            }
        }

        [Fact]
        public void InvalidAccessedPathExcludedFromAnalysis()
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new TestFileMonitoringViolationAnalyzer(LoggingContext, context, graph);

            var declaredOuput = CreateAbsolutePath(context, X("/X/out/declared"));           

            var process = graph.AddProcess(declaredOuput);

            var result = analyzer.AnalyzePipViolations(
                process,
                new[]
                {
                    CreateViolationForInvalidPath(RequestedAccess.Read, X("c/invalid:name_1.txt"))
                },
                new[]
                {
                    CreateViolationForInvalidPath(RequestedAccess.Read, X("c/invalid:name_2.txt"))
                },
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: null,
                allowedUndeclaredReads: null,
                absentPathProbesUnderOutputDirectories: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                out _);
            
            XAssert.IsTrue(result.IsViolationClean);
        }

        private static AbsolutePath CreateAbsolutePath(BuildXLContext context, string path)
        {
            return AbsolutePath.Create(context.PathTable, path);
        }

        private ReportedFileAccess CreateViolation(RequestedAccess access, AbsolutePath path)
        {
            var process = new ReportedProcess(1, ReportedExecutablePath);

            return ReportedFileAccess.Create(
                ReportedFileOperation.CreateFile,
                process,
                access,
                FileAccessStatus.Denied,
                explicitlyReported: false,
                error: 0,
                usn: ReportedFileAccess.NoUsn,
                desiredAccess: DesiredAccess.GENERIC_READ,
                shareMode: ShareMode.FILE_SHARE_NONE,
                creationDisposition: CreationDisposition.OPEN_ALWAYS,
                flagsAndAttributes: FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                path: path);
        }

        private ReportedFileAccess CreateViolationForInvalidPath(RequestedAccess access, string path)
        {
            var process = new ReportedProcess(1, ReportedExecutablePath);

            return new ReportedFileAccess(ReportedFileOperation.CreateFile,
                process,
                access,
                FileAccessStatus.Denied,
                explicitlyReported: false,
                error: 0,
                usn: ReportedFileAccess.NoUsn,
                desiredAccess: DesiredAccess.GENERIC_READ,
                shareMode: ShareMode.FILE_SHARE_NONE,
                creationDisposition: CreationDisposition.OPEN_ALWAYS,
                flagsAndAttributes: FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                AbsolutePath.Invalid,
                path: path,
                enumeratePatttern: null);
        }
    }

    internal class TestFileMonitoringViolationAnalyzer : FileMonitoringViolationAnalyzer
    {
        public readonly HashSet<DependencyViolation> CollectedViolations = new HashSet<DependencyViolation>();
        public readonly HashSet<DependencyViolation> AssertedViolations = new HashSet<DependencyViolation>();
        private bool m_doLogging;
        private bool m_collectNonErrorViolations;

        public TestFileMonitoringViolationAnalyzer(LoggingContext loggingContext, PipExecutionContext context, IQueryablePipDependencyGraph graph, bool doLogging = false, bool collectNonErrorViolations = true)
            : base(loggingContext, context, graph, new QueryableEmptyFileContentManager(), validateDistribution: false, ignoreDynamicWritesOnAbsentProbes: false, unexpectedFileAccessesAsErrors: true)
        {
            m_doLogging = doLogging;
            m_collectNonErrorViolations = collectNonErrorViolations;
        }

        protected override ReportedViolation HandleDependencyViolation(
            DependencyViolationType violationType,
            AccessLevel accessLevel,
            AbsolutePath path,
            Process violator,
            bool isWhitelistedViolation,
            Pip related,
            AbsolutePath processPath)
        {
            ReportedViolation reportedViolation = new ReportedViolation(true, violationType, path, violator.PipId, related?.PipId, processPath);
            if (m_doLogging)
            {
                reportedViolation = base.HandleDependencyViolation(violationType, accessLevel, path, violator, isWhitelistedViolation, related, processPath);
            }

            // Always collect error violations, and also collect other non-errors if asked to.
            if (reportedViolation.IsError || m_collectNonErrorViolations)
            {
                bool added = CollectedViolations.Add(
                    new DependencyViolation(
                        violationType,
                        accessLevel,
                        path,
                        violator,
                        related
                        ));

                XAssert.IsTrue(added, "Duplicate violation reported");
            }

            return reportedViolation;
        }

        public void AssertNoExtraViolationsCollected()
        {
            if (CollectedViolations.Count == 0)
            {
                return;
            }

            var messageBuilder = new StringBuilder();
            messageBuilder.AppendLine("Collected violations that were not expected (via AssertContainsViolation): ");
            foreach (DependencyViolation collected in CollectedViolations)
            {
                messageBuilder.AppendFormat("\t{0}\n", collected.ToString(Context.PathTable));
            }

            XAssert.Fail(messageBuilder.ToString());
        }

        public void AssertContainsViolation(DependencyViolation expected, string message)
        {
            if (CollectedViolations.Remove(expected))
            {
                AssertedViolations.Add(expected);
                return;
            }

            if (AssertedViolations.Contains(expected))
            {
                return;
            }

            var messageBuilder = new StringBuilder();
            messageBuilder.AppendFormat(
                "Expected a violation for path {0}, but it was not reported: {1}\n",
                expected.Path.ToString(Context.PathTable),
                message);
            messageBuilder.AppendFormat("\tExpected {0}\n", expected.ToString(Context.PathTable));

            messageBuilder.AppendLine("Collected violations for the same path: ");
            foreach (DependencyViolation collected in CollectedViolations)
            {
                if (collected.Path == expected.Path)
                {
                    messageBuilder.AppendFormat("\t{0}\n", collected.ToString(Context.PathTable));
                }
            }

            XAssert.Fail(messageBuilder.ToString());
        }
    }

    internal class DependencyViolation : IEquatable<DependencyViolation>
    {
        public readonly FileMonitoringViolationAnalyzer.DependencyViolationType ViolationType;
        public readonly FileMonitoringViolationAnalyzer.AccessLevel AccessLevel;
        public readonly AbsolutePath Path;
        public readonly Process Violator;
        public readonly Pip Related;

        public DependencyViolation(
            FileMonitoringViolationAnalyzer.DependencyViolationType violationType,
            FileMonitoringViolationAnalyzer.AccessLevel accessLevel,
            AbsolutePath path,
            Process violator,
            Pip related)
        {
            ViolationType = violationType;
            AccessLevel = accessLevel;
            Path = path;
            Violator = violator;
            Related = related;
        }

        public string ToString(PathTable pathTable)
        {
            return
                I($"[{ViolationType:G} due to {AccessLevel:G} access on {Path.ToString(pathTable)} - violator ID {Violator.PipId} / related ID {(Related == null ? PipId.Invalid : Related.PipId)}]");
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as DependencyViolation);
        }

        public bool Equals(DependencyViolation other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return ViolationType == other.ViolationType && 
                   AccessLevel == other.AccessLevel && 
                   Path == other.Path && 
                   Violator == other.Violator &&
                   Related == other.Related;
        }

        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(
                ViolationType.GetHashCode(),
                AccessLevel.GetHashCode(),
                Path.GetHashCode(),
                Violator.GetHashCode(),
                Related == null ? 0 : Related.GetHashCode());
        }
    }
}
