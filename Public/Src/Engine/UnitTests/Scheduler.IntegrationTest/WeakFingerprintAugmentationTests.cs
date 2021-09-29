// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    public class WeakFingerprintAugmentationTests : SchedulerIntegrationTestBase
    {
        public WeakFingerprintAugmentationTests(ITestOutputHelper output) : base(output)
        {
        }

        /// <summary>
        /// The basic idea of this test is to create a pip for which each invocation generates a new path set.
        /// The pip reads a file A which prompts it to read another file B_x. 
        /// In iteration 0 it reads { A, B_0 }, in iteration 1 { A, B_1 }, ... and so forth.
        /// 
        /// This tests behavior for augmenting weak fingerprints whereby the fingerprint eventually gets augmented with (at the very
        /// least A) and thus changes for every iteration over the threshold.
        /// </summary>
        /// <remarks>
        /// TODO: This test failed on Linux. This test passes when `Analysis.IgnoreResult(e.ToArray())` in the enumerate operation is removed.
        /// </remarks>
        [TheoryIfSupported(requiresWindowsOrMacOperatingSystem: true)]
        [InlineData(true)]
        [InlineData(false)]
        public void AugmentedWeakFingerprint(bool augmentWeakFingerprint)
        {
            const int Threshold = 3;
            const int Iterations = 7;

            if (Configuration.Schedule.IncrementalScheduling)
            {
                // Test relies on cache info which would not be available when running with incremental scheduling
                // since the pip may be skipped
                return;
            }

            var sealDirectoryPath = CreateUniqueDirectory(ObjectRoot);
            var path = sealDirectoryPath.ToString(Context.PathTable);
            var fileA = CreateSourceFile(path);

            DirectoryArtifact dir = SealDirectory(sealDirectoryPath, SealDirectoryKind.SourceAllDirectories);

            var ops = new Operation[]
            {
                Operation.EnumerateDir(dir),
                Operation.ReadFileFromOtherFile(fileA, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            var fileBPathByIteration = Enumerable.Range(0, Iterations).Select(i => CreateSourceFile(path)).Select(s => s.Path.ToString(Context.PathTable)).ToArray();

            var lastFileBPath = fileBPathByIteration.Last();

            var builder = CreatePipBuilder(ops);
            builder.AddInputDirectory(dir);
            Process pip = SchedulePipBuilder(builder).Process;

            if (augmentWeakFingerprint)
            {
                Configuration.Cache.AugmentWeakFingerprintPathSetThreshold = Threshold;
            }

            HashSet<WeakContentFingerprint> weakFingerprints = new HashSet<WeakContentFingerprint>();
            List<WeakContentFingerprint> orderedWeakFingerprints = new List<WeakContentFingerprint>();
            HashSet<ContentHash> pathSetHashes = new HashSet<ContentHash>();
            List<ContentHash> orderedPathSetHashes = new List<ContentHash>();

            // Part 1: Ensure that we get cache misses and generate new augmented weak fingerprints when
            // over the threshold
            for (int i = 0; i < fileBPathByIteration.Length; i++)
            {
                // Indicate to from file A that file B_i should be read
                File.WriteAllText(path: fileA.Path.ToString(Context.PathTable), contents: fileBPathByIteration[i]);

                var result = RunScheduler().AssertCacheMiss(pip.PipId);

                var weakFingerprint = result.RunData.ExecutionCachingInfos[pip.PipId].WeakFingerprint;
                bool addedWeakFingerprint = weakFingerprints.Add(weakFingerprint);

                // Record the weak fingerprints so in the second phase we can check that the cache lookups get
                // hits against the appropriate fingerprints
                orderedWeakFingerprints.Add(weakFingerprint);

                if (augmentWeakFingerprint)
                {
                    if (i >= Threshold)
                    {
                        Assert.True(addedWeakFingerprint, "Weak fingerprint should keep changing when over the threshold.");
                    }
                    else if (i > 0)
                    {
                        Assert.False(addedWeakFingerprint, "Weak fingerprint should NOT keep changing when under the threshold.");
                    }
                }
                else
                {
                    Assert.True(weakFingerprints.Count == 1, "Weak fingerprint should not change unless weak fingerprint augmentation is enabled.");
                }

                ContentHash pathSetHash = result.RunData.ExecutionCachingInfos[pip.PipId].PathSetHash;
                bool addedPathSet = pathSetHashes.Add(pathSetHash);
                Assert.True(addedPathSet, "Every invocation should have a unique path set.");

                // Record the path sets so in the second phase we can check that the cache lookups get
                // hits with the appropriate path sets
                orderedPathSetHashes.Add(pathSetHash);
            }

            // Part 2: Test behavior of multiple strong fingerprints for the same augmented weak fingerprint

            // Indicate to from file A that the last file B_i should be read
            File.WriteAllText(path: fileA.Path.ToString(Context.PathTable), contents: lastFileBPath);

            HashSet<StrongContentFingerprint> strongFingerprints = new HashSet<StrongContentFingerprint>();

            for (int i = 0; i < Iterations; i++)
            {
                // Change content of file B
                File.WriteAllText(path: lastFileBPath, contents: Guid.NewGuid().ToString());

                var executionResult = RunScheduler().AssertCacheMiss(pip.PipId);
                var weakFingerprint = executionResult.RunData.ExecutionCachingInfos[pip.PipId].WeakFingerprint;
                ContentHash pathSetHash = executionResult.RunData.ExecutionCachingInfos[pip.PipId].PathSetHash;
                var executionStrongFingerprint = executionResult.RunData.ExecutionCachingInfos[pip.PipId].StrongFingerprint;
                var addedStrongFingerprint = strongFingerprints.Add(executionStrongFingerprint);

                // Weak fingerprint should not change since file B should not be in the augmenting path set
                Assert.Equal(expected: orderedWeakFingerprints.Last(), actual: weakFingerprint);

                // Set of paths is not changing
                Assert.Equal(expected: orderedPathSetHashes.Last(), actual: pathSetHash);

                Assert.True(addedStrongFingerprint, "New strong fingerprint should be computed since file B has unique content");

                var cacheHitResult = RunScheduler().AssertCacheHit(pip.PipId);

                weakFingerprint = cacheHitResult.RunData.CacheLookupResults[pip.PipId].WeakFingerprint;
                pathSetHash = cacheHitResult.RunData.CacheLookupResults[pip.PipId].GetCacheHitData().PathSetHash;
                var cacheLookupStrongFingerprint = cacheHitResult.RunData.CacheLookupResults[pip.PipId].GetCacheHitData().StrongFingerprint;

                // Weak fingerprint should not change since file B should not be in path set
                Assert.Equal(expected: orderedWeakFingerprints.Last(), actual: weakFingerprint);

                // Weak fingerprint should not change since file B should not be in path set
                Assert.Equal(expected: orderedPathSetHashes.Last(), actual: pathSetHash);

                // Should get a hit against the strong fingerprint just added above
                Assert.Equal(expected: executionStrongFingerprint, actual: cacheLookupStrongFingerprint);
            }

            // Part 3: Ensure that we get cache hits when inputs are the same
            for (int i = 0; i < fileBPathByIteration.Length; i++)
            {
                // Indicate to from file A that file B_i should be read
                File.WriteAllText(path: fileA.Path.ToString(Context.PathTable), contents: fileBPathByIteration[i]);

                // We should get a hit for the same inputs
                var result = RunScheduler().AssertCacheHit(pip.PipId);

                // Weak fingerprint should be the same as the first run with this configuration (i.e. the
                // augmented fingerprint when over the threshold)
                var weakFingerprint = result.RunData.CacheLookupResults[pip.PipId].WeakFingerprint;
                Assert.Equal(expected: orderedWeakFingerprints[i], actual: weakFingerprint);

                // Path set should be the same as the first run with this configuration
                ContentHash pathSetHash = result.RunData.CacheLookupResults[pip.PipId].GetCacheHitData().PathSetHash;
                Assert.Equal(expected: orderedPathSetHashes[i], actual: pathSetHash);
            }
        }

        [Fact]
        public void AugmentedPathSetUsageTracking()
        {
            Configuration.Cache.AugmentWeakFingerprintPathSetThreshold = 2;
            Configuration.Cache.MonitorAugmentedPathSets = 5;

            var directory = CreateUniqueDirectoryArtifact();
            var firstSourceFile = CreateSourceFile(directory.Path);
            var secondSourceFile = CreateSourceFile(directory.Path);
            var readFileA = CreateSourceFile(directory.Path);
            var readFileB = CreateSourceFile(directory.Path);
            var readFileC = CreateSourceFile(directory.Path);
            var suspiciousFile = CreateSourceFile(directory.Path);
            var suspiciousFilePath = ArtifactToString(suspiciousFile);
            var absentFile = CreateOutputFileArtifact(directory.Path);

            var suspiciousFileContent = Guid.NewGuid().ToString();
            File.WriteAllText(suspiciousFilePath, suspiciousFileContent);

            var sealedDirectory = CreateAndScheduleSealDirectoryArtifact(
                directory.Path,
                SealDirectoryKind.SourceAllDirectories);

            var builder = CreatePipBuilder(new[]
            {
                Operation.ReadFileFromOtherFile(firstSourceFile, doNotInfer: true),
                Operation.ReadFileFromOtherFile(secondSourceFile, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            builder.AddInputDirectory(sealedDirectory);
            var process = SchedulePipBuilder(builder).Process;

            // source_1 -> A, source_2 -> suspiciousFile
            File.WriteAllText(ArtifactToString(firstSourceFile), ArtifactToString(readFileA));
            File.WriteAllText(ArtifactToString(secondSourceFile), suspiciousFilePath);
            RunScheduler().AssertCacheMiss(process.PipId);

            // source_1 -> B, source_2 -> suspiciousFile
            File.WriteAllText(ArtifactToString(firstSourceFile), ArtifactToString(readFileB));
            RunScheduler().AssertCacheMiss(process.PipId);

            // source_1 -> C, source_2 -> suspiciousFile
            // The augmented pathset created before running the pip -> {source_1, source_2, suspiciousFile}
            File.WriteAllText(ArtifactToString(firstSourceFile), ArtifactToString(readFileC));
            RunScheduler().AssertCacheMiss(process.PipId);

            // source_1 -> C, source_2 -> absentFile (no changes to suspiciousFile's content)            
            // New #{source_2} -> new #{Augmented PathSet} -> new augmented weak FP -> cache miss
            File.WriteAllText(ArtifactToString(secondSourceFile), ArtifactToString(absentFile));
            RunScheduler().AssertCacheMiss(process.PipId);

            // suspiciousFile was not accessed by the pip -> the message must be logged
            AssertVerboseEventLogged(LogEventId.SuspiciousPathsInAugmentedPathSet, count: 1, allowMore: false);
            var logMessages = EventListener.GetLogMessagesForEventId((int)LogEventId.SuspiciousPathsInAugmentedPathSet);
            XAssert.IsTrue(logMessages.Length == 1);
            var logMessage = logMessages[0];
            var suspiciousFileContentHashString = ContentHashingUtilities.HashString(suspiciousFileContent).ToShortString();
            // Check that we logged the right file, and the right hash value.
            XAssert.Contains(logMessage, new[] { suspiciousFilePath, suspiciousFileContentHashString });

            // No changes -> cache hit (suspicious file is present).
            RunScheduler().AssertCacheHit(process.PipId);
            // We are not checking for suspicious paths in the augmented pathset when there is a cache hit.
            AssertVerboseEventLogged(LogEventId.SuspiciousPathsInAugmentedPathSet, count: 0, allowMore: false);

            // Any changes to suspicious files' content cause a cache miss (unless IncrementalScheduling is enabled).
            suspiciousFileContent = Guid.NewGuid().ToString();
            File.WriteAllText(suspiciousFilePath, suspiciousFileContent);

            if (Configuration.Schedule.IncrementalScheduling)
            {
                RunScheduler().AssertCacheHit(process.PipId);
            }
            else
            {
                RunScheduler().AssertCacheMiss(process.PipId);

                AssertVerboseEventLogged(LogEventId.SuspiciousPathsInAugmentedPathSet, count: 1, allowMore: false);
                logMessages = EventListener.GetLogMessagesForEventId((int)LogEventId.SuspiciousPathsInAugmentedPathSet);
                // Checking for "== 2" and not "== 1" because GetLogMessagesForEventId does not reset EventListener. 
                XAssert.IsTrue(logMessages.Length == 2);
                logMessage = logMessages[1];
                suspiciousFileContentHashString = ContentHashingUtilities.HashString(suspiciousFileContent).ToShortString();
                XAssert.Contains(logMessage, new[] { suspiciousFilePath, suspiciousFileContentHashString });
            }

            // The removal of a suspicions file causes a cache miss (unless IncrementalScheduling is enabled).
            File.Delete(suspiciousFilePath);
            XAssert.IsFalse(File.Exists(suspiciousFilePath));

            if (Configuration.Schedule.IncrementalScheduling)
            {
                RunScheduler().AssertCacheHit(process.PipId);
            }
            else
            {
                RunScheduler().AssertCacheMiss(process.PipId);

                AssertVerboseEventLogged(LogEventId.SuspiciousPathsInAugmentedPathSet, count: 1, allowMore: false);
                logMessages = EventListener.GetLogMessagesForEventId((int)LogEventId.SuspiciousPathsInAugmentedPathSet);
                // Checking for "== 3" and not "== 1" because GetLogMessagesForEventId does not reset EventListener. 
                XAssert.IsTrue(logMessages.Length == 3);
                logMessage = logMessages[2];
                XAssert.Contains(logMessage, new[] { suspiciousFilePath, WellKnownContentHashes.AbsentFile.ToShortString() });

                // No changes -> cache hit (suspicious file is absent).
                RunScheduler().AssertCacheHit(process.PipId);
                // We are not checking for suspicious paths in the augmented pathset when there is a cache hit.
                AssertVerboseEventLogged(LogEventId.SuspiciousPathsInAugmentedPathSet, count: 0, allowMore: false);
            }
        }
    }
}
