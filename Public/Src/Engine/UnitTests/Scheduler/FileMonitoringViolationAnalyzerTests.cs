// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Scheduler.FileMonitoringViolationAnalyzer;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Tests for <see cref="FileMonitoringViolationAnalyzer"/> and <see cref="DependencyViolationEventSink"/> on a stubbed <see cref="IQueryablePipDependencyGraph"/>.
    /// </summary>
    public class FileMonitoringViolationAnalyzerTests : XunitBuildXLTest
    {
        private readonly string ReportedExecutablePath = X("/X/bin/tool.exe");
        private readonly string JunkPath = X("/X/out/junk");
        private readonly string DoubleWritePath = X("/X/out/doublewrite");
        private readonly string ProducedPath = X("/X/out/produced");

        public FileMonitoringViolationAnalyzerTests(ITestOutputHelper output)
            : base(output) 
        {
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Pips.ETWLogger.Log);
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
                dynamicObservations: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
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
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
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
                dynamicObservations: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
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
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
        }

        [Fact]
        public void DoubleWriteEventLoggedOnViolationError()
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new FileMonitoringViolationAnalyzer(LoggingContext, context, graph, new QueryableEmptyFileContentManager(), validateDistribution: false, ignoreDynamicWritesOnAbsentProbes: DynamicWriteOnAbsentProbePolicy.IgnoreNothing, unexpectedFileAccessesAsErrors: true);

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
                dynamicObservations: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
                out _);

            AssertVerboseEventLogged(LogEventId.DependencyViolationDoubleWrite);
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
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
                ignoreDynamicWritesOnAbsentProbes: DynamicWriteOnAbsentProbePolicy.IgnoreNothing);

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
                dynamicObservations: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
                out _);

            AssertVerboseEventLogged(LogEventId.DependencyViolationDoubleWrite);

            if (unexpectedFileAccessesAsErrors)
            {
                AssertErrorEventLogged(LogEventId.FileMonitoringError);
            }
            else
            {
                AssertWarningEventLogged(LogEventId.FileMonitoringWarning);
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
                dynamicObservations: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
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
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
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
                dynamicObservations: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
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
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
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
                dynamicObservations: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
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
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
        }

        [Fact]
        public void ReadRaceEventLoggedOnViolation()
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new FileMonitoringViolationAnalyzer(LoggingContext, context, graph, new QueryableEmptyFileContentManager(), validateDistribution: false, ignoreDynamicWritesOnAbsentProbes: DynamicWriteOnAbsentProbePolicy.IgnoreNothing, unexpectedFileAccessesAsErrors: true);

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
                dynamicObservations: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
                out _);
            AssertVerboseEventLogged(LogEventId.DependencyViolationReadRace);
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
        }

        [Fact]
        public void ReadRaceEventLoggedOnViolationDistributed()
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new FileMonitoringViolationAnalyzer(LoggingContext, context, graph, new QueryableEmptyFileContentManager(), validateDistribution: true, ignoreDynamicWritesOnAbsentProbes: DynamicWriteOnAbsentProbePolicy.IgnoreNothing, unexpectedFileAccessesAsErrors: true);

            AbsolutePath violatorOutput = CreateAbsolutePath(context, JunkPath);
            AbsolutePath producerOutput = CreateAbsolutePath(context, ProducedPath);

            Process producer = graph.AddProcess(producerOutput);
            Process violator = graph.AddProcess(violatorOutput);
            graph.SetConcurrentRange(producer, violator);

            AnalyzePipViolationsResult analyzePipViolationsResult = 
                analyzer.AnalyzePipViolations(
                    violator,
                    new[] { CreateViolation(RequestedAccess.Read, producerOutput) },
                    null, // allowlisted accesses
                    exclusiveOpaqueDirectoryContent: null,
                    sharedOpaqueDirectoryWriteAccesses: null,
                    allowedUndeclaredReads: null,
                    dynamicObservations: null,
                    ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                    CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
                    out _); 

            AssertTrue(!analyzePipViolationsResult.IsViolationClean);
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
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
                dynamicObservations: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
                out _);

            AssertVerboseEventLogged(LogEventId.DependencyViolationUndeclaredOrderedRead);
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
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
                dynamicObservations: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
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
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
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
                dynamicObservations: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
                out _);

            analyzer.AssertContainsViolation(
                new DependencyViolation(
                    FileMonitoringViolationAnalyzer.DependencyViolationType.UndeclaredOutput,
                    FileMonitoringViolationAnalyzer.AccessLevel.Write,
                    undeclaredOutput,
                    producer,
                    null),
                "The violator has an undeclared output but it wasn't reported.");
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
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
                dynamicObservations: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
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
                dynamicObservations: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
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
                dynamicObservations: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
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
            AssertErrorEventLogged(LogEventId.FileMonitoringError, 3);
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
                dynamicObservations: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
                out _);

            AssertErrorEventLogged(LogEventId.FileMonitoringError);

            // The ending of the expected log message.
            // The order of files here is based on the sorting in FileMonitoringViolationAnalyzer.AggregateAccessViolationPaths()
            StringBuilder expectedLog = new StringBuilder();
            expectedLog.AppendLine("- Disallowed file accesses were detected (R = read, W = write):");
            // process that 'caused' the violations
            expectedLog.AppendLine($"Disallowed file accesses observed in process tree with root: {ReportedExecutablePath}");
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
                Analysis.IgnoreResult(violation.RenderForDFASummary(new PipId(1), testContext.PathTable));
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

            string result = FileMonitoringViolationAnalyzer.AggregateAccessViolationPaths(new PipId(1), violations, testContext.PathTable, (pipId) => "dummy");
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

            string result = FileMonitoringViolationAnalyzer.AggregateAccessViolationPaths(new PipId(1), violations, testContext.PathTable, (pipId) => "PLACEHOLDER PIP DESCRIPTION");

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
        [InlineData(RewritePolicy.DoubleWritesAreErrors)]
        [InlineData(RewritePolicy.UnsafeFirstDoubleWriteWins)]
        public void DoubleWritePolicyDeterminesViolationSeverity(RewritePolicy doubleWritePolicy)
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
                dynamicObservations: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
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
            if (doubleWritePolicy == RewritePolicy.DoubleWritesAreErrors)
            {
                AssertErrorEventLogged(LogEventId.FileMonitoringError);
            }
            else
            {
                AssertWarningEventLogged(LogEventId.FileMonitoringWarning);
            }
        }

        [Theory]
        [InlineData(RewritePolicy.DoubleWritesAreErrors)]
        [InlineData(RewritePolicy.AllowSameContentDoubleWrites)]
        public void DoubleWritePolicyIsContentAware(RewritePolicy doubleWritePolicy)
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
            var doubleWriteOutputArtifact = FileArtifact.CreateOutputFile(doubleWriteOutput);
            ContentHash contentHash = ContentHashingUtilities.CreateRandom();
            var fileContentInfo = new FileContentInfo(contentHash, contentHash.Length);
            var outputsContent = new (FileArtifact, FileMaterializationInfo, PipOutputOrigin)[]
                {
                    (doubleWriteOutputArtifact, new FileMaterializationInfo(
                        fileContentInfo, 
                        doubleWriteOutput.GetName(context.PathTable), 
                        opaqueDirectoryRoot: AbsolutePath.Invalid, 
                        dynamicOutputCaseSensitiveRelativeDirectory: RelativePath.Invalid), 
                    PipOutputOrigin.NotMaterialized)
                }.ToReadOnlyArray();
            var sharedOpaqueRoot = doubleWriteOutput.GetParent(context.PathTable);
            var sharedOpaqueDirectoryWriteAccesses = new Dictionary<AbsolutePath, IReadOnlyCollection<FileArtifactWithAttributes>> { [sharedOpaqueRoot] = 
                new FileArtifactWithAttributes[] { FileArtifactWithAttributes.Create(doubleWriteOutputArtifact, FileExistence.Required) } };

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
                dynamicObservations: null,
                outputsContent,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
                out _);

            analyzer.AnalyzePipViolations(
                violator,
                new ReportedFileAccess[0],
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: sharedOpaqueDirectoryWriteAccesses,
                allowedUndeclaredReads: null,
                dynamicObservations: null,
                outputsContent,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
                out _);

            // Based on the double write policy, the violation is an error or it is not raised
            if ((doubleWritePolicy & RewritePolicy.DoubleWritesAreErrors) != 0)
            {
                AssertErrorEventLogged(LogEventId.FileMonitoringError);
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
                dynamicObservations: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
                out _);
            
            XAssert.IsTrue(result.IsViolationClean);
        }

        [Theory]
        [InlineData("sub1/sub2/file.txt", "sub1/sub2/file.txt", DynamicWriteOnAbsentProbePolicy.IgnoreNothing, true)]
        [InlineData("sub1/sub2/file.txt", "sub1/sub2/file.txt", DynamicWriteOnAbsentProbePolicy.IgnoreFileProbes, false)]
        [InlineData("sub1/sub2/file.txt", "sub1/sub2/file.txt", DynamicWriteOnAbsentProbePolicy.IgnoreDirectoryProbes, true)]
        [InlineData("sub1/sub2/file.txt", "sub1/sub2/file.txt", DynamicWriteOnAbsentProbePolicy.IgnoreAll, false)]
        //
        [InlineData("sub1/sub2/file.txt", "sub1/sub2", DynamicWriteOnAbsentProbePolicy.IgnoreNothing, true)]
        [InlineData("sub1/sub2/file.txt", "sub1/sub2", DynamicWriteOnAbsentProbePolicy.IgnoreFileProbes, true)]
        [InlineData("sub1/sub2/file.txt", "sub1/sub2", DynamicWriteOnAbsentProbePolicy.IgnoreDirectoryProbes, false)]
        [InlineData("sub1/sub2/file.txt", "sub1/sub2", DynamicWriteOnAbsentProbePolicy.IgnoreAll, false)]
        //
        [InlineData("sub1/sub2/file.txt", "sub1", DynamicWriteOnAbsentProbePolicy.IgnoreNothing, true)]
        [InlineData("sub1/sub2/file.txt", "sub1", DynamicWriteOnAbsentProbePolicy.IgnoreFileProbes, true)]
        [InlineData("sub1/sub2/file.txt", "sub1", DynamicWriteOnAbsentProbePolicy.IgnoreDirectoryProbes, false)]
        [InlineData("sub1/sub2/file.txt", "sub1", DynamicWriteOnAbsentProbePolicy.IgnoreAll, false)]
        public void WriteOnAbsentPathProbeTests(string writeRelPath, string probeRelPath, DynamicWriteOnAbsentProbePolicy policy, bool isViolation)
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new TestFileMonitoringViolationAnalyzer(LoggingContext, context, graph, policy: policy);

            var sod = X("/x/out/sod");
            var sodPath = CreateAbsolutePath(context, sod);
            var probePath = CreateAbsolutePath(context, X($"{sod}/{probeRelPath}"));
            var writePath = CreateAbsolutePath(context, X($"{sod}/{writeRelPath}"));
            var prober = graph.AddProcess(CreateAbsolutePath(context, X("/x/out/prober-out.txt")));

            analyzer.AnalyzePipViolations(
                prober,
                violations: null,
                allowlistedAccesses: null,
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: null,
                allowedUndeclaredReads: null,
                dynamicObservations: new ReadOnlyHashSet<(AbsolutePath, DynamicObservationKind)>(new[] { (probePath, DynamicObservationKind.AbsentPathProbeUnderOutputDirectory) }),
                outputsContent: ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
                out _);

            var producer = graph.AddProcess(CreateAbsolutePath(context, X("/x/out/producer-out.txt")));
            var sodWrites = new Dictionary<AbsolutePath, IReadOnlyCollection<FileArtifactWithAttributes>>
            {
                [sodPath] = new[] { FileArtifactWithAttributes.Create(FileArtifact.CreateOutputFile(writePath), FileExistence.Required) }
            };

            analyzer.AnalyzePipViolations(
                producer,
                violations: null,
                allowlistedAccesses: null,
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: sodWrites,
                allowedUndeclaredReads: null,
                dynamicObservations: null,
                outputsContent: ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
                out _);

            if (isViolation)
            {
                analyzer.AssertContainsViolation(
                    new DependencyViolation(
                        DependencyViolationType.WriteOnAbsentPathProbe,
                        AccessLevel.Write,
                        probePath,
                        violator: producer,
                        related: prober),
                    "The violator created a file/directory after absent path probe but it wasn't reported.");
                AssertErrorEventLogged(LogEventId.FileMonitoringError);
            }

            analyzer.AssertNoExtraViolationsCollected();
        }

        /// <summary>
        /// Verifies logging of a doublewrite dependency violation by a process pip after a writefile pip has written to a file.
        /// The purpose of this test is to verify that dependency violations for writefile pips are handled properly.
        /// </summary>
        [Fact]
        public void DoubleWriteViolationWithWriteFilePip()
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new TestFileMonitoringViolationAnalyzer(LoggingContext, context, graph, doLogging: true);

            AbsolutePath violatorOutput = CreateAbsolutePath(context, JunkPath);
            AbsolutePath producerOutput = CreateAbsolutePath(context, DoubleWritePath);

            WriteFile producer = graph.AddWriteFilePip(producerOutput);
            Process violator = graph.AddProcess(violatorOutput);

            var res = analyzer.AnalyzePipViolations(
                violator,
                new[] { CreateViolation(RequestedAccess.ReadWrite, producerOutput) },
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: null,
                allowedUndeclaredReads: null,
                dynamicObservations: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
                out _);

            analyzer.AssertContainsViolation(
                new DependencyViolation(
                    FileMonitoringViolationAnalyzer.DependencyViolationType.DoubleWrite,
                    FileMonitoringViolationAnalyzer.AccessLevel.Write,
                    producerOutput,
                    violator,
                    producer),
                "The violator has an undeclared output but it wasn't reported.");

            AssertErrorEventLogged(LogEventId.FileMonitoringError);
            analyzer.AssertNoExtraViolationsCollected();
        }

        /// <summary>
        /// Verifies logging of a UndeclaredOrderedRead dependency violation by a process pip after a copyfile pip has written to a file.
        /// The purpose of this test is to verify that dependency violations for copyfile pips are handled properly.
        /// </summary>
        [Fact]
        public void ReadUndeclaredOutputViolationWithCopyFilePip()
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new TestFileMonitoringViolationAnalyzer(LoggingContext, context, graph, doLogging: true);

            AbsolutePath violatorOutput = CreateAbsolutePath(context, JunkPath);
            AbsolutePath producerOutput = CreateAbsolutePath(context, ProducedPath);
            FileArtifact producerSourceArtifact = FileArtifact.CreateSourceFile(CreateAbsolutePath(context, ProducedPath));
            FileArtifact producerDestinationArtifact = FileArtifact.CreateSourceFile(producerOutput);

            CopyFile producer = graph.AddCopyFilePip(producerSourceArtifact, producerDestinationArtifact);
            Process violator = graph.AddProcess(violatorOutput);

            var res = analyzer.AnalyzePipViolations(
                violator,
                new[] { CreateViolation(RequestedAccess.Read, producerOutput) },
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: null,
                allowedUndeclaredReads: null,
                dynamicObservations: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
                out _);

            analyzer.AssertContainsViolation(
                new DependencyViolation(
                    FileMonitoringViolationAnalyzer.DependencyViolationType.UndeclaredOrderedRead,
                    FileMonitoringViolationAnalyzer.AccessLevel.Read,
                    producerOutput,
                    violator,
                    producer),
                "The violator has an undeclared read on the producer but it wasn't reported.");

            AssertErrorEventLogged(LogEventId.FileMonitoringError);
            analyzer.AssertNoExtraViolationsCollected();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void DynamicWriteInStaticallyDeclaredSourceFile(bool enableSafeSourceRewrites)
        {
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var graph = new QueryablePipDependencyGraph(context);
            var analyzer = new TestFileMonitoringViolationAnalyzer(LoggingContext, context, graph, doLogging: true);

            AbsolutePath sourcePath = CreateAbsolutePath(context, ProducedPath);
            AbsolutePath violatorOutput = CreateAbsolutePath(context, JunkPath);

            var hashSourceFile = graph.AddSourceFile(sourcePath);
            Process violator = graph.AddProcess(violatorOutput,
                enableSafeSourceRewrites ? RewritePolicy.SafeSourceRewritesAreAllowed : RewritePolicy.DoubleWritesAreErrors,
                dependencies: new List<FileArtifact>() { hashSourceFile.Artifact });

            // Simulate a dynamic write on the source path
            var sharedOpaqueDirectoryWriteAccesses = new Dictionary<AbsolutePath, IReadOnlyCollection<FileArtifactWithAttributes>>()
            {
                [sourcePath] = new[] { FileArtifactWithAttributes.Create(FileArtifact.CreateOutputFile(sourcePath), FileExistence.Required) }
            };

            var res = analyzer.AnalyzePipViolations(
                violator,
                new[] { CreateViolation(RequestedAccess.Write, sourcePath) },
                new ReportedFileAccess[0],
                exclusiveOpaqueDirectoryContent: null,
                sharedOpaqueDirectoryWriteAccesses: sharedOpaqueDirectoryWriteAccesses,
                allowedUndeclaredReads: null,
                dynamicObservations: null,
                ReadOnlyArray<(FileArtifact, FileMaterializationInfo, PipOutputOrigin)>.Empty,
                CollectionUtilities.EmptyDictionary<AbsolutePath, RequestedAccess>(),
                out _);
            
            analyzer.AssertContainsViolation(
                new DependencyViolation(
                    FileMonitoringViolationAnalyzer.DependencyViolationType.WriteInStaticallyDeclaredSourceFile,
                    FileMonitoringViolationAnalyzer.AccessLevel.Write,
                    sourcePath,
                    violator,
                    hashSourceFile),
                "The violator writes into a statically declared source file, but it wasn't reported.");

            AssertErrorEventLogged(LogEventId.FileMonitoringError);
            
            if (enableSafeSourceRewrites)
            {
                AssertWarningEventLogged(LogEventId.SafeSourceRewriteNotAvailableForStaticallyDeclaredSources);
            }

            analyzer.AssertNoExtraViolationsCollected();
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
                rawError: 0,
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
                rawError: 0,
                usn: ReportedFileAccess.NoUsn,
                desiredAccess: DesiredAccess.GENERIC_READ,
                shareMode: ShareMode.FILE_SHARE_NONE,
                creationDisposition: CreationDisposition.OPEN_ALWAYS,
                flagsAndAttributes: FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                AbsolutePath.Invalid,
                path: path,
                enumeratePattern: null);
        }
    }

    internal class TestFileMonitoringViolationAnalyzer : FileMonitoringViolationAnalyzer
    {
        public readonly HashSet<DependencyViolation> CollectedViolations = new HashSet<DependencyViolation>();
        public readonly HashSet<DependencyViolation> AssertedViolations = new HashSet<DependencyViolation>();
        private bool m_doLogging;
        private bool m_collectNonErrorViolations;

        public TestFileMonitoringViolationAnalyzer(LoggingContext loggingContext, PipExecutionContext context, IQueryablePipDependencyGraph graph, bool doLogging = false, bool collectNonErrorViolations = true, DynamicWriteOnAbsentProbePolicy policy = DynamicWriteOnAbsentProbePolicy.IgnoreNothing)
            : base(loggingContext, context, graph, new QueryableEmptyFileContentManager(), validateDistribution: false, ignoreDynamicWritesOnAbsentProbes: policy, unexpectedFileAccessesAsErrors: true)
        {
            m_doLogging = doLogging;
            m_collectNonErrorViolations = collectNonErrorViolations;
        }

        protected override ReportedViolation HandleDependencyViolation(
            DependencyViolationType violationType,
            AccessLevel accessLevel,
            AbsolutePath path,
            Pip violator,
            bool isAllowlistedViolation,
            Pip related,
            AbsolutePath processPath)
        {
            ReportedViolation reportedViolation = new ReportedViolation(true, violationType, path, violator.PipId, related?.PipId, processPath);
            if (m_doLogging)
            {
                reportedViolation = base.HandleDependencyViolation(violationType, accessLevel, path, violator, isAllowlistedViolation, related, processPath);
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
        public readonly Pip Violator;
        public readonly Pip Related;

        public DependencyViolation(
            FileMonitoringViolationAnalyzer.DependencyViolationType violationType,
            FileMonitoringViolationAnalyzer.AccessLevel accessLevel,
            AbsolutePath path,
            Pip violator,
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
