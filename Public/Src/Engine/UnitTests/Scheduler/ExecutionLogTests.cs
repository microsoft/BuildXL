// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Tests for the execution log
    /// </summary>
    [Trait("Category", "ExecutionLogTests")]
    public sealed class ExecutionLogTests : ProcessReportingTestBase
    {
        public ExecutionLogTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestRoundTripExecutionLog()
        {
            TestExecutionLogHelper(verifier => ExpectProcessMonitoringEventData(verifier));
        }

        [Fact]
        public void TestSkipUnhandledEvents()
        {
            TestExecutionLogHelper(verifier =>
            {
                verifier.SkipUnhandledEvents = true;

                verifier.Expect(new ProcessExecutionMonitoringReportedEventData()
                {
                    PipId = new PipId(12)
                });

                verifier.LogUnexpectedExpect(new ProcessExecutionMonitoringReportedEventData()
                {
                    PipId = new PipId(34)
                });
            });
        }

        [Fact]
        public void TestPipCacheMissEventData()
        {
            TestExecutionLogHelper(verifier =>
            {
                verifier.Expect(new PipCacheMissEventData()
                {
                    PipId = new PipId(123),
                    CacheMissType = PipCacheMissType.MissForDescriptorsDueToWeakFingerprints,
                });
            });
        }

        [Fact]
        public void TestPipExecutionDirectoryOutputs()
        {
            TestExecutionLogHelper(verifier =>
                                   {
                                       verifier.Expect(new PipExecutionDirectoryOutputs
                                                       {
                                                           PipId = new PipId(123),
                                                           DirectoryOutputs = ReadOnlyArray<(DirectoryArtifact, ReadOnlyArray<FileArtifact>)>.FromWithoutCopy(
                                                               (
                                                                   CreateDirectory(), 
                                                                   ReadOnlyArray<FileArtifact>.FromWithoutCopy(CreateSourceFile(), CreateOutputFile())
                                                               ),
                                                               (
                                                                   CreateDirectory(),
                                                                   ReadOnlyArray<FileArtifact>.FromWithoutCopy(CreateOutputFile(), CreateSourceFile())
                                                               )
                                                            )
                                                       });
                                   });
        }

        [Fact]
        public void TestObservedInputsLoggedForFingerprintComputation()
        {
            TestExecutionLogHelper(verifier =>
            {
                var pipId = new PipId(234);
                var oi1 = new ObservedInputsEventData()
                {
                    PipId = pipId,
                    ObservedInputs = ReadOnlyArray<ObservedInput>.FromWithoutCopy(
                        ObservedInput.CreateAbsentPathProbe(RandomPath()),
                        ObservedInput.CreateDirectoryEnumeration(
                            RandomPath(),
                            new DirectoryFingerprint(ContentHashingUtilities.CreateRandom()),
                            isSearchPath: true),
                        ObservedInput.CreateFileContentRead(RandomPath(), ContentHashingUtilities.CreateRandom()),
                        ObservedInput.CreateExistingDirectoryProbe(RandomPath()))
                };

                var oi2 = new ObservedInputsEventData()
                {
                    PipId = pipId,
                    ObservedInputs = ReadOnlyArray<ObservedInput>.FromWithoutCopy(
                        ObservedInput.CreateAbsentPathProbe(RandomPath()),
                        ObservedInput.CreateExistingDirectoryProbe(RandomPath()),
                        ObservedInput.CreateDirectoryEnumeration(
                            RandomPath(),
                            new DirectoryFingerprint(ContentHashingUtilities.CreateRandom()),
                            isSearchPath: true))
                };

                var fingerprintComputationEvent = new ProcessFingerprintComputationEventData()
                {
                    Kind = FingerprintComputationKind.CacheCheck,
                    WeakFingerprint = RandomWeakFingerprint(),
                    PipId = new PipId(234),
                    StrongFingerprintComputations = new[]
                    {
                        new ProcessStrongFingerprintComputationData(
                            ContentHashingUtilities.CreateRandom(),
                            new List<StrongContentFingerprint>(),
                            RandomPathSet())
                        {
                            IsStrongFingerprintHit = true
                        }.ToSuccessfulResult(
                            RandomStrongFingerprint(),
                            oi1.ObservedInputs),

                        new ProcessStrongFingerprintComputationData(
                            ContentHashingUtilities.CreateRandom(),
                            new List<StrongContentFingerprint>(),
                            RandomPathSet())
                        {
                            IsStrongFingerprintHit = false
                        }.ToSuccessfulResult(
                            RandomStrongFingerprint(),
                            oi2.ObservedInputs),

                        new ProcessStrongFingerprintComputationData(
                            ContentHashingUtilities.CreateRandom(),
                            new List<StrongContentFingerprint>(),
                            RandomPathSet())
                        {
                            IsStrongFingerprintHit = false
                        }
                    }
                };

                // Ensure observed inputs are logged
                verifier.ExpectUnlogged(oi1);
                verifier.ExpectUnlogged(oi2);

                // Ensure fingerprint computation is handled
                verifier.Expect(fingerprintComputationEvent);
            });
        }

        private ObservedPathSet RandomPathSet()
        {
            var emptyObservedAccessFileNames = SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer>.FromSortedArrayUnsafe(
                ReadOnlyArray<StringId>.Empty,
                new CaseInsensitiveStringIdComparer(Context.StringTable));

            return new ObservedPathSet(
                SortedReadOnlyArray<ObservedPathEntry, ObservedPathEntryExpandedPathComparer>
                .CloneAndSort(ReadOnlyArray<ObservedPathEntry>.FromWithoutCopy(
                        new ObservedPathEntry(
                            RandomPath(), 
                            ObservedPathEntryFlags.DirectoryEnumeration | ObservedPathEntryFlags.IsDirectoryPath | ObservedPathEntryFlags.DirectoryEnumerationWithCustomPattern, 
                            RegexDirectoryMembershipFilter.ConvertWildcardsToRegex("*.txt")),
                        new ObservedPathEntry(RandomPath(), ObservedPathEntryFlags.DirectoryEnumeration | ObservedPathEntryFlags.IsDirectoryPath, null),
                        new ObservedPathEntry(RandomPath(), ObservedPathEntryFlags.None, null),
                        new ObservedPathEntry(RandomPath(), ObservedPathEntryFlags.IsSearchPath, null),
                        new ObservedPathEntry(RandomPath(), ObservedPathEntryFlags.IsDirectoryPath, null)
                    ),
                    new ObservedPathEntryExpandedPathComparer(Context.PathTable.ExpandedPathComparer)),
                emptyObservedAccessFileNames,
                new UnsafeOptions(UnsafeOptions.SafeConfigurationValues, ContentHashingUtilities.CreateRandom()));
        }

        private AbsolutePath RandomPath()
        {
            return AbsolutePath.Create(Context.PathTable, A("x", "mydir", Guid.NewGuid().ToString()));
        }

        private WeakContentFingerprint RandomWeakFingerprint()
        {
            return new WeakContentFingerprint(FingerprintUtilities.CreateRandom());
        }

        private StrongContentFingerprint RandomStrongFingerprint()
        {
            return new StrongContentFingerprint(FingerprintUtilities.CreateRandom());
        }

        private void TestExecutionLogHelper(Action<ExecutionLogVerifier> logVerification)
        {
            PipExecutionContext context = Context;
            Guid logId = Guid.NewGuid();
            using (var stream = new MemoryStream())
            {
                using (var verifier = new ExecutionLogVerifier(this))
                {
                    using (var binaryLogger = new BinaryLogger(stream, context, logId, closeStreamOnDispose: false))
                    using (var logFile = new ExecutionLogFileTarget(binaryLogger, closeLogFileOnDispose: false))
                    {
                        verifier.Target = logFile;

                        logVerification(verifier);
                    }

                    stream.Position = 0;

                    using (var logReader = new ExecutionLogFileReader(stream, context, verifier, closeStreamOnDispose: false))
                    {
                        logReader.ReadAllEvents();
                    }
                }
            }
        }

        private void ExpectProcessMonitoringEventData(ExecutionLogVerifier verifier)
        {
            var reportedProcesses = new ReportedProcess[]
                {
                    CreateRandomReportedProcess(),
                    CreateRandomReportedProcess(),
                    CreateRandomReportedProcess(),
                    CreateRandomReportedProcess(),
                };

            // Case: Try with all data
            verifier.Expect(new ProcessExecutionMonitoringReportedEventData()
            {
                PipId = new PipId(12),

                // Explicitly using n 
                ReportedProcesses = new ReportedProcess[]
                {
                    reportedProcesses[0],
                    reportedProcesses[2],
                    reportedProcesses[3],
                    CreateRandomReportedProcess(),
                    CreateRandomReportedProcess()
                },
                ReportedFileAccesses = new ReportedFileAccess[]
                {
                    CreateRandomReportedFileAccess(),
                    CreateRandomReportedFileAccess(reportedProcesses[2]),
                    CreateRandomReportedFileAccess(reportedProcesses[0]),
                    CreateRandomReportedFileAccess(),
                    CreateRandomReportedFileAccess(),
                    CreateRandomReportedFileAccess(),
                }
            });

            // Case: Try without reported processes
            verifier.Expect(new ProcessExecutionMonitoringReportedEventData()
            {
                PipId = new PipId(12),

                ReportedFileAccesses = new ReportedFileAccess[]
                {
                    CreateRandomReportedFileAccess(),
                    CreateRandomReportedFileAccess(),
                    CreateRandomReportedFileAccess(),
                    CreateRandomReportedFileAccess(),
                    CreateRandomReportedFileAccess(),
                    CreateRandomReportedFileAccess(),
                }
            });

            // Case: Try without reported file accesses
            verifier.Expect(new ProcessExecutionMonitoringReportedEventData()
            {
                PipId = new PipId(12),

                // Explicitly using n 
                ReportedProcesses = new ReportedProcess[]
                {
                    reportedProcesses[0],
                    reportedProcesses[2],
                    reportedProcesses[3],
                    CreateRandomReportedProcess(),
                    CreateRandomReportedProcess()
                },
            });
        }

        /// <summary>
        /// Verifies data written and read from execution log is equivalent
        /// NOTE: All calls to log to original execution log should go through Expect
        /// </summary>
        private class ExecutionLogVerifier : ExecutionLogTargetBase, 
            IEqualityVerifier<ProcessExecutionMonitoringReportedEventData>,
            IEqualityVerifier<ProcessFingerprintComputationEventData>,
            IEqualityVerifier<ObservedInputsEventData>,
            IEqualityVerifier<PipCacheMissEventData>,
            IEqualityVerifier<PipExecutionDirectoryOutputs>
        {
            private readonly Queue<object> m_expectedData = new Queue<object>();
            private readonly ExecutionLogTests m_parent;
            public IExecutionLogTarget Target;

            public bool SkipUnhandledEvents = false;

            public ExecutionLogVerifier(ExecutionLogTests parent)
            {
                m_parent = parent;
            }

            protected override void ReportUnhandledEvent<TEventData>(TEventData data)
            {
                VerifyEvent(data);

                if (SkipUnhandledEvents)
                {
                    base.ReportUnhandledEvent<TEventData>(data);
                }
            }

            private void VerifyEvent<TEventData>(TEventData data)
            {
                var verifier = AssertIsVerifier<TEventData>();
                Assert.NotEmpty(m_expectedData);
                var expectedData = m_expectedData.Dequeue();

                Assert.Equal(typeof(TEventData), expectedData.GetType());
                TEventData expectedEventData = (TEventData)expectedData;

                Assert.True(verifier.VerifyEquals(expectedEventData, data));
            }

            public override void ObservedInputs(ObservedInputsEventData data)
            {
                VerifyEvent(data);
            }

            public override void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data)
            {
                VerifyEvent(data);
            }

            public virtual void Expect<TEventData>(TEventData data)
                where TEventData : struct, IExecutionLogEventData<TEventData>
            {
                Contract.Requires(Target != null);
                AssertIsVerifier<TEventData>();
                m_expectedData.Enqueue(data);
                data.Metadata.LogToTarget(data, Target);
            }

            public virtual void ExpectUnlogged<TEventData>(TEventData data)
            {
                Contract.Requires(Target != null);
                AssertIsVerifier<TEventData>();
                m_expectedData.Enqueue(data);
            }

            public virtual void LogUnexpectedExpect<TEventData>(TEventData data)
                where TEventData : struct, IExecutionLogEventData<TEventData>
            {
                Contract.Requires(Target != null);
                AssertIsVerifier<TEventData>();
                data.Metadata.LogToTarget(data, Target);
            }

            private IEqualityVerifier<TEventData> AssertIsVerifier<TEventData>()
            {
                IEqualityVerifier<TEventData> verifier = this as IEqualityVerifier<TEventData>;
                Assert.True(verifier != null, "Execution log verifier must be a verifier for the given type");
                return verifier;
            }

            public override void Dispose()
            {
                Assert.Empty(m_expectedData);
            }

            public bool VerifyEquals(ProcessExecutionMonitoringReportedEventData expected, ProcessExecutionMonitoringReportedEventData actual)
            {
                AssertSequenceEqual(
                    expected.ReportedFileAccesses ?? CollectionUtilities.EmptyArray<ReportedFileAccess>(),
                    actual.ReportedFileAccesses ?? CollectionUtilities.EmptyArray<ReportedFileAccess>(),
                    (expectedFileAccess, actualFileAccess) => AssertEqual(expectedFileAccess, actualFileAccess));

                if (expected.ReportedProcesses != null && expected.ReportedProcesses.Count != 0)
                {
                    HashSet<(uint, string)> actualProcessSet = 
                        new HashSet<(uint id, string path)>(actual.ReportedProcesses.Select(rp => (rp.ProcessId, rp.Path)));

                    HashSet<(uint, string)> actualProcessArgsSet =
                        new HashSet<(uint, string)>(actual.ReportedProcesses.Select(rp => (rp.ProcessId, rp.ProcessArgs)));

                    foreach (var expectedReportedProcess in expected.ReportedProcesses)
                    {
                        Assert.True(actualProcessSet.Contains((expectedReportedProcess.ProcessId, expectedReportedProcess.Path)));
                        Assert.True(actualProcessArgsSet.Contains((expectedReportedProcess.ProcessId, expectedReportedProcess.ProcessArgs)));
                    }
                }

                return true;
            }

            public bool VerifyEquals(ObservedInputsEventData expected, ObservedInputsEventData actual)
            {
                XAssert.AreEqual(expected.PipId, actual.PipId);

                return VerifyEquals(expected.ObservedInputs, actual.ObservedInputs);
            }

            public bool VerifyEquals(ReadOnlyArray<ObservedInput> expected, ReadOnlyArray<ObservedInput> actual)
            {
                XAssert.AreEqual(expected.IsValid, actual.IsValid);

                if (!expected.IsValid)
                {
                    return true;
                }

                AssertSequenceEqual(
                    expected,
                    actual,
                    (expectedInput, actualInput) =>
                    {
                        AssertEqual(expectedInput, actualInput,
                            o => o.Hash,
                            o => o.IsDirectoryPath,
                            o => o.IsSearchPath,
                            o => o.DirectoryEnumeration,
                            o => o.Type,
                            o => o.Path);
                    });

                return true;
            }

            public bool VerifyEquals(ReadOnlyArray<ObservedPathEntry> expected, ReadOnlyArray<ObservedPathEntry> actual)
            {
                XAssert.AreEqual(expected.IsValid, actual.IsValid);

                if (!expected.IsValid)
                {
                    return true;
                }

                AssertSequenceEqual(
                    expected,
                    actual,
                    (expectedInput, actualInput) =>
                    {
                        AssertEqual(expectedInput, actualInput,
                            o => o.IsDirectoryPath,
                            o => o.IsSearchPath,
                            o => o.DirectoryEnumeration,
                            o => o.Path);
                    });

                return true;
            }

            public bool VerifyEquals(ProcessFingerprintComputationEventData expected, ProcessFingerprintComputationEventData actual)
            {
                XAssert.AreEqual(expected.PipId, actual.PipId);
                XAssert.AreEqual(expected.WeakFingerprint, actual.WeakFingerprint);
                XAssert.AreEqual(expected.Kind, actual.Kind);

                AssertSequenceEqual(expected.StrongFingerprintComputations, actual.StrongFingerprintComputations,
                    (expectedStrongComputation, actualStrongComputation) =>
                    {
                        XAssert.AreEqual(expectedStrongComputation.PathSetHash, actualStrongComputation.PathSetHash);
                        XAssert.AreEqual(expectedStrongComputation.Succeeded, actualStrongComputation.Succeeded);
                        VerifyEquals(expectedStrongComputation.PathEntries, actualStrongComputation.PathEntries);

                        if (expectedStrongComputation.Succeeded)
                        {
                            XAssert.AreEqual(expectedStrongComputation.IsStrongFingerprintHit, actualStrongComputation.IsStrongFingerprintHit);
                            XAssert.AreEqual(expectedStrongComputation.ComputedStrongFingerprint, actualStrongComputation.ComputedStrongFingerprint);
                            AssertSequenceEqual(
                                expectedStrongComputation.PriorStrongFingerprints,
                                actualStrongComputation.PriorStrongFingerprints,
                                (expectedPrint, actualPrint) => XAssert.AreEqual(expectedPrint, actualPrint));
                            VerifyEquals(expectedStrongComputation.ObservedInputs, actualStrongComputation.ObservedInputs);
                        }
                    });

                return true;
            }

            public bool VerifyEquals(PipCacheMissEventData expected, PipCacheMissEventData actual)
            {
                XAssert.AreEqual(expected.PipId, actual.PipId);
                XAssert.AreEqual(expected.CacheMissType, actual.CacheMissType);

                return true;
            }

            public bool VerifyEquals(PipExecutionDirectoryOutputs expected, PipExecutionDirectoryOutputs actual)
            {
                XAssert.AreEqual(expected.PipId, actual.PipId);
                XAssert.AreSetsEqual(expected.DirectoryOutputs, actual.DirectoryOutputs, expectedResult: true, DirectoryOutputComparer.Instance);
                return true;
            }

            private class DirectoryOutputComparer : IEqualityComparer<(DirectoryArtifact directoryArtifact, ReadOnlyArray<FileArtifact> contents)>
            {
                public static readonly DirectoryOutputComparer Instance = new DirectoryOutputComparer();

                private DirectoryOutputComparer()
                {}

                public bool Equals((DirectoryArtifact directoryArtifact, ReadOnlyArray<FileArtifact> contents) right, (DirectoryArtifact directoryArtifact, ReadOnlyArray<FileArtifact> contents) left)
                {
                    return right.directoryArtifact.Equals(left.directoryArtifact) && right.contents.SequenceEqual(left.contents);
                }

                public int GetHashCode((DirectoryArtifact directoryArtifact, ReadOnlyArray<FileArtifact> contents) obj)
                {
                    var contentsHash = HashCodeHelper.Combine(obj.contents, fileArtifact => fileArtifact.GetHashCode());
                    return (int) HashCodeHelper.Combine(obj.directoryArtifact.GetHashCode(), contentsHash);
                }
            }
        }

        private interface IEqualityVerifier<in T>
        {
            bool VerifyEquals(T expected, T actual);
        }
    }
}
