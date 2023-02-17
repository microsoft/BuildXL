// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    public class DirectoryMembershipFingerprinterTests : TemporaryStorageTestBase
    {
        private readonly BuildXLContext m_context;
        
        /// <summary>
        /// Initialize the test
        /// </summary>
        public DirectoryMembershipFingerprinterTests(ITestOutputHelper output) : base(output)
        {
            m_context = BuildXLContext.CreateInstanceForTesting();

            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Pips.ETWLogger.Log);
        }

        [Fact]
        public void DirectoryFingerprintForNonexistentAndEmptyDirectoriesInFilesystemIsNull()
        {
            var nonexistentDirectory = ComputeDirectoryFingerprint(m_context, X("/Z/fake"), new string[] { });
            XAssert.AreEqual(DirectoryFingerprint.Zero, nonexistentDirectory);

            var emptyDirectory = ComputeDirectoryFingerprint(m_context, X("/Z/fake"), null);
            XAssert.AreEqual(DirectoryFingerprint.Zero, emptyDirectory);
        }

        [Fact]
        public void DirectoryFingerprintRules()
        {
            // Test that files are ignored appropriately
            var ruleSet = new DirectoryMembershipFingerprinterRuleSet(new[]
            {
                new DirectoryMembershipFingerprinterRule("TestRule1", AbsolutePath.Create(m_context.PathTable, X("/Z/fake")), false, new[] { "ignore" }, false),
                new DirectoryMembershipFingerprinterRule("TestRule2", AbsolutePath.Create(m_context.PathTable, X("/Z")), false, new[] { "ignoreRec" }, true),
            });

            XAssert.IsTrue(ruleSet.TryGetRule(m_context.PathTable, AbsolutePath.Create(m_context.PathTable, X("/Z/fake")), out DirectoryMembershipFingerprinterRule rule));
            XAssert.IsNotNull(rule);

            var firstFingerprint = ComputeDirectoryFingerprint(m_context, X("/Z/fake"), new string[] { X("/Z/fake/file1") });
            var secondFingerprint = ComputeDirectoryFingerprint(m_context, X("/Z/fake"), new string[] { X("/Z/fake/ignoreRec"), X("/Z/fake/file1"), X("/Z/fake/ignore") }, rule);
            XAssert.IsTrue(firstFingerprint.Hash.Equals(secondFingerprint.Hash));
        }

        private DirectoryFingerprint ComputeDirectoryFingerprint(BuildXLContext context, string path, IEnumerable<string> directoryMembers, DirectoryMembershipFingerprinterRule rule = null)
        {
            Func<EnumerationRequest, PathExistence?> enumerate
                = (request) =>
                {
                    if (directoryMembers == null)
                    {
                        return PathExistence.Nonexistent;
                    }

                    foreach (var item in directoryMembers)
                    {
                        request.HandleEntry(AbsolutePath.Create(context.PathTable, item), System.IO.Path.GetFileName(item));
                    }

                    return PathExistence.ExistsAsDirectory;
                };

            var process = CreateDummyProcessWithInputs(Array.Empty<FileArtifact>(), context);

            var dirPath = AbsolutePath.Create(context.PathTable, path);
            DirectoryMembershipFingerprinter fingerprinter = new DirectoryMembershipFingerprinter(LoggingContext, context);
            var fp = fingerprinter.TryComputeDirectoryFingerprint(
                directoryPath: dirPath,
                cachePipInfo: CacheableProcess.GetProcessCacheInfo(process, context),
                tryEnumerateDirectory: enumerate,
                cacheableFingerprint: false,
                rule: rule,
                eventData: new DirectoryMembershipHashedEventData()
                    {
                        Directory = dirPath,
                        IsStatic = false
                    });
            XAssert.IsTrue(fp.HasValue);

            return fp.Value;
        }

        private static int s_procCounter = 1;
        public static Process CreateDummyProcessWithInputs(IEnumerable<FileArtifact> inputs, BuildXLContext context)
        {
            FileArtifact output = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, @"\\FAKEPATH\output" + s_procCounter + ".txt")).CreateNextWrittenVersion();
            s_procCounter++;
            FileArtifact exe = FileArtifact.CreateSourceFile(AbsolutePath.Create(context.PathTable, @"\\FAKEPATH\tool.exe"));

            var pipDataBuilder = new PipDataBuilder(context.StringTable);
            return new Process(
                executable: exe,
                workingDirectory: AbsolutePath.Create(context.PathTable, @"\\FAKEPATH\"),
                arguments: pipDataBuilder.ToPipData(" ", PipDataFragmentEscaping.NoEscaping),
                responseFile: FileArtifact.Invalid,
                responseFileData: PipData.Invalid,
                environmentVariables: ReadOnlyArray<EnvironmentVariable>.Empty,
                standardInput: FileArtifact.Invalid,
                standardOutput: FileArtifact.Invalid,
                standardError: FileArtifact.Invalid,
                standardDirectory: output.Path.GetParent(context.PathTable),
                warningTimeout: null,
                timeout: null,
                dependencies: ReadOnlyArray<FileArtifact>.From(inputs.Union(new[] { exe })),
                outputs: ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(output.WithAttributes()),
                directoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty,
                directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                orderDependencies: ReadOnlyArray<PipId>.Empty,
                untrackedPaths: ReadOnlyArray<AbsolutePath>.Empty,
                untrackedScopes: ReadOnlyArray<AbsolutePath>.Empty,
                tags: ReadOnlyArray<StringId>.Empty,
                successExitCodes: ReadOnlyArray<int>.Empty,
                semaphores: ReadOnlyArray<ProcessSemaphoreInfo>.Empty,
                provenance: PipProvenance.CreateDummy(context),
                toolDescription: StringId.Invalid,
                additionalTempDirectories: ReadOnlyArray<AbsolutePath>.Empty);
        }
    }
}
