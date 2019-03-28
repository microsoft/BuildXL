// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Engine
{
    /// <summary>
    /// Incremental build tests for <see cref="BuildXL.Utilities.DirectoryArtifact"/> dependencies that are partially-sealed
    /// (such that runtime monitoring may indicate disallowed accesses in a directory dependency).
    /// </summary>
    public sealed class PartiallySealedDirectoryArtifactIncrementalBuildTests : IncrementalBuildTestBase
    {
        public PartiallySealedDirectoryArtifactIncrementalBuildTests(ITestOutputHelper output)
            : base(output)
        {
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);
        }

        [Fact]
        public void CleanBuildReportsNoCacheHits()
        {
            SetupTestStateWithDefaultOptions();
            EagerCleanBuild("Build #1");
        }

        [Fact]
        public void DeletingUsedInput()
        {
            SetupTestStateWithDefaultOptions();
            EagerCleanBuild("Build #1");

            var buildPaths = GetBuildPaths();
            File.Delete(buildPaths.HeaderDirectoryBPaths[0]);

            EagerCleanBuild("Build #2");

            EagerBuildWithoutChanges("Build #3");
        }

        [Fact]
        public void FailureWhenAccessingUnsealedFile()
        {
            SetupTestStateWithDefaultOptions();
            var buildPaths = GetBuildPaths();
            File.WriteAllText(buildPaths.HeaderAUnsealedSiblingPath, "Oops!");
            FailedBuild("Build #1");
            AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess, buildPaths.HeaderAUnsealedSiblingPath);
            AssertVerboseEventLogged(EventId.DisallowedFileAccessInSealedDirectory);
            AssertDependencyViolationMissingSourceDependency(count: 1);
            AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            AssertErrorEventLogged(EventId.FileMonitoringError);
        }

        /// <summary>
        /// Tests the scenario in which a subsequent build declares a smaller partial-seal for a pip:
        /// - Run a pip with access to some directory artifact D, accessing F under D
        /// - Replace D with a partial seal D' not containing F
        /// - Should re-run and fail after accessing F.
        /// </summary>
        [Fact]
        public void FailureWhenRerunningWithSmallerSealUsed()
        {
            SetupTestState(options: GetSpecOptions(useEmptyIncA: false));
            var buildPaths = GetBuildPaths();
            EagerCleanBuild("Build #1");

            // a.h remains - but now we re-run and should find that the now-used empty partial seal doesn't contain it anymore.
            SetupTestState(options: GetSpecOptions(useEmptyIncA: true));
            FailedBuild("Build #2");
            AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess, buildPaths.HeaderAPath);
            AssertVerboseEventLogged(EventId.DisallowedFileAccessInSealedDirectory);
            AssertDependencyViolationMissingSourceDependency();
            AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            AssertErrorEventLogged(EventId.FileMonitoringError);
        }

        [Fact]
        public void FailureWhenAccessingUnsealedInIncrementalBuild()
        {
            SetupTestStateWithDefaultOptions();
            var buildPaths = GetBuildPaths();
            EagerCleanBuild("Build #1");
            File.WriteAllText(buildPaths.HeaderAUnsealedSiblingPath, "Oops!");
            FailedBuild("Build #2");
            AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess, buildPaths.HeaderAUnsealedSiblingPath);
            AssertVerboseEventLogged(EventId.DisallowedFileAccessInSealedDirectory);
            AssertDependencyViolationMissingSourceDependency();
            AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            AssertErrorEventLogged(EventId.FileMonitoringError);
        }

        [Fact]
        public void FailureWhenAccessingMultipleUnsealedFiles()
        {
            SetupTestStateWithDefaultOptions();
            var buildPaths = GetBuildPaths();
            string xPath = Path.Combine(buildPaths.HeaderDirectoryBRootPath, "x");
            File.WriteAllText(xPath, "Oops!");
            string yPath = Path.Combine(buildPaths.HeaderDirectoryBRootPath, "y");
            File.WriteAllText(yPath, "Oops!!");
            FailedBuild("Build #1");
            AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess, xPath);
            AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess, yPath);
            AssertVerboseEventLogged(EventId.DisallowedFileAccessInSealedDirectory, count: 2);
            AssertDependencyViolationMissingSourceDependency(count: 1);
            AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            AssertErrorEventLogged(EventId.FileMonitoringError);
        }

        /// <summary>
        /// Tests that enumeration under a partial seal results in a directory membership assertion, such that
        /// tools may re-run (and fail) due to accessing a newly-added file not within their partial seals.
        /// </summary>
        [Fact]
        public void FailureWhenAccessingMultipleUnsealedFilesInIncrementalBuild()
        {
            SetupTestStateWithDefaultOptions();
            EagerCleanBuild("Build #1");
            var buildPaths = GetBuildPaths();
            string xPath = Path.Combine(buildPaths.HeaderDirectoryBRootPath, "x");
            File.WriteAllText(xPath, "Oops!");
            string yPath = Path.Combine(buildPaths.HeaderDirectoryBRootPath, "y");
            File.WriteAllText(yPath, "Oops!!");
            FailedBuild("Build #2");
            AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess, xPath);
            AssertVerboseEventLogged(EventId.PipProcessDisallowedFileAccess, yPath);
            AssertVerboseEventLogged(EventId.DisallowedFileAccessInSealedDirectory, count: 2);
            AssertDependencyViolationMissingSourceDependency(count: 1);
            AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            AssertErrorEventLogged(EventId.FileMonitoringError);
        }

        private void AssertDependencyViolationMissingSourceDependency(int count = 1)
        {
            // TODO: figure out why this check is flaky on macOS after OssRename
            if (!OperatingSystemHelper.IsUnixOS)
            {
                AssertVerboseEventLogged(LogEventId.DependencyViolationMissingSourceDependency, count, allowMore: true);
            }
        }

        /// <summary>
        /// Gets the named bool options to populate 'options.ds'.
        /// UseEmptyIncA: Causes {IncAEmpty} to be used in place of {IncA}. Relevant for testing that the *particular* directory artifact is enforced, not the largest known.
        /// </summary>
        private Dictionary<string, bool> GetSpecOptions(bool useEmptyIncA = false)
        {
            return new Dictionary<string, bool>() { { "useEmptyIncA", useEmptyIncA} };
        }

        private void SetupTestStateWithDefaultOptions()
        {
            SetupTestState(options: GetSpecOptions());
        }

        protected override string GetSpecContents()
        {
            var combineCmd = OperatingSystemHelper.IsUnixOS
                ? "-c \" if [[ -f inc/a/unsealed.h ]]; then /bin/cat inc/a/unsealed.h; fi ; /bin/cat inc/a/a.h ; /usr/bin/find -s inc/b -type f -exec /bin/cat {} \\\\; \""
                : @"/d /c (if exist inc\\a\\unsealed.h (type inc\\a\\unsealed.h)) & type inc\\a\\a.h & (for /R inc\\b %f in (*) do @type ""%f"")";
            var echoCmd = OperatingSystemHelper.IsUnixOS
                ? "-c \" if [[ -f obj/inc/absent.h ]]; then /bin/cat obj/inc/absent.h; fi ; /bin/cat obj/inc/combined.h \""
                : @"/d /c (if exist obj\\inc\\absent.h (type obj\\inc\\absent.h) else (type obj\\inc\\combined.h))";

            return $@"
import {{Artifact, Cmd, Tool, Transformer}} from 'Sdk.Transformers';

const incA = Transformer.sealPartialDirectory(d`inc/a`, [
    f`inc/a/a.h`
]);

const incAEmpty = Transformer.sealPartialDirectory(d`inc/a`, [
]);

const incB = Transformer.sealPartialDirectory(d`inc/b`, [ 
    f`inc/b/1/b1.h`, 
    f`inc/b/2/b2.h`, 
    f`inc/b/3/b3.h`, 
    f`inc/b/4/b4.h`, 
]);

{GetExecuteFunction()}

const combineHeaders = execute({{
    tool: {GetOsShellCmdToolDefinition()},
    workingDirectory: d`.`,
    arguments: [
        Cmd.rawArgument('{combineCmd}'),
    ],
    dependencies: [
        ...(useEmptyIncA ? [incAEmpty] : [incA]),
        incB,
    ],
    consoleOutput: p`obj/inc/combined.h`,
}});

const outInc = Transformer.sealPartialDirectory(d`obj/inc`, [
    combineHeaders.getOutputFile(p`obj/inc/combined.h`),
]);
   
const echoCombined = execute({{
    tool: {GetOsShellCmdToolDefinition()},
    workingDirectory: d`.`,
    arguments: [
        Cmd.rawArgument('{echoCmd}'),
    ],
    dependencies: [
        outInc,
    ],
    consoleOutput: p`obj/final.out`,
}});
";
        }

        /// <summary>
        /// Number of pips that would run on a clean build.
        /// </summary>
        protected override int TotalPips => 2;

        /// <summary>
        /// Number of outputs that would be copied from the cache on a fully-cached build.
        /// </summary>
        protected override int TotalPipOutputs => 2;

        private Paths GetBuildPaths()
        {
            var sourceDirectoryPath = Configuration.Layout.SourceDirectory.ToString(PathTable);
            var objectDirectoryPath = Configuration.Layout.ObjectDirectory.ToString(PathTable);
            return new Paths
            {
                HeaderAPath = Path.Combine(sourceDirectoryPath, X("inc/a/a.h")),
                HeaderAUnsealedSiblingPath = Path.Combine(sourceDirectoryPath, X("inc/a/unsealed.h")),
                HeaderDirectoryBRootPath = Path.Combine(sourceDirectoryPath, X("inc/b")),
                HeaderDirectoryBPaths = GetRelativePaths().Select(path => Path.Combine(sourceDirectoryPath, path)).ToArray(),

                // Outputs follow.
                CombinedHeaderPath = Path.Combine(objectDirectoryPath, X("inc/combined.h")),
                FinalOutputPath = Path.Combine(objectDirectoryPath, "final.out"),
            };
        }

        private static IEnumerable<string> GetRelativePaths()
        {
            return Enumerable.Range(1, 4)
                .Select(
                    i =>
                        Path.Combine(
                            X("inc/b"),
                            i.ToString(CultureInfo.InvariantCulture),
                            "b" + i.ToString(CultureInfo.InvariantCulture) + ".h"));
        }

        protected override void WriteInitialSources()
        {
            AddFile(X("inc/a/a.h"), "A");

            foreach (string headerPath in GetRelativePaths())
            {
                AddFile(headerPath, headerPath);
            }
        }

        protected override void VerifyOutputsAfterBuild(IConfiguration config, PathTable pathTable)
        {
            var expectedContentsBuilder = new StringBuilder();
            var buildPath = GetBuildPaths();
            if (File.Exists(buildPath.HeaderAPath))
            {
                expectedContentsBuilder.Append(File.ReadAllText(buildPath.HeaderAPath));
            }

            foreach (string headerBPath in buildPath.HeaderDirectoryBPaths)
            {
                if (File.Exists(headerBPath))
                {
                    expectedContentsBuilder.Append(File.ReadAllText(headerBPath));
                }
            }

            string expectedContents = expectedContentsBuilder.ToString();

            XAssert.IsTrue(File.Exists(buildPath.CombinedHeaderPath), "Combined header output missing");
            XAssert.IsTrue(File.Exists(buildPath.FinalOutputPath), "Final output missing");

            XAssert.AreEqual(expectedContents, File.ReadAllText(buildPath.CombinedHeaderPath).TrimEnd(), "Incorrect contents for combined.h");
            XAssert.AreEqual(expectedContents, File.ReadAllText(buildPath.FinalOutputPath).TrimEnd(), "Incorrect contents for final.out (but its only input was correct)");
        }

        public sealed class Paths
        {
            public string HeaderAPath;
            public string HeaderAUnsealedSiblingPath;
            public string HeaderDirectoryBRootPath;
            public string[] HeaderDirectoryBPaths;
            public string CombinedHeaderPath;
            public string FinalOutputPath;
        }
    }
}
