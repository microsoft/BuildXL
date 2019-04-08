// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Engine
{
    public sealed class IncrementalBuildTests : IncrementalBuildTestBase
    {
        private const string SourceFile1Contents = "One one one.";
        private const string SourceFile2Contents = "Two two two!";

        public IncrementalBuildTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void CleanBuildReportsNoCacheHits()
        {
            SetupTestState();
            EagerCleanBuild("Build #1");
        }

        [Fact]
        public void IncrementalBuildWithoutChangesUsesCachedOutputs()
        {
            SetupTestState();
            EagerCleanBuild("Build #1");
            EagerBuildWithoutChanges("Build #2");
        }

        [Fact]
        public void IncrementalBuildWithIdenticalSourcesRewrittenUsesCachedOutputs()
        {
            SetupTestState();
            EagerCleanBuild("Build #1");

            // We write the same sources as last time, but the USNs and timestamps will be higher.
            // Since the content is identical, so should be the content and semantic fingerprints.
            SetupTestState();

            EagerBuildWithoutChanges("Build #2");
        }

        [Fact]
        public void IncrementalBuildRunsPipsImpactedFromChangedSources()
        {
            SetupTestState();
            EagerCleanBuild("Build #1");

            // SourceFile2 is used by the second process, but not the first. Only the second process run.
            Paths buildPaths = GetBuildPaths();
            File.WriteAllText(buildPaths.SourceFile2Path, "Hey! Look at this new input!");

            BuildCounters counters = Build("Build #2");
            counters.VerifyNumberOfPipsExecuted(1);
            counters.VerifyNumberOfProcessPipsCached(TotalPips - 1);
            VerifyNumberOfCachedOutputs(counters, totalUpToDate: 1, totalCopied: 0);

            // Despite being a partial build, results from the single pip run should have been cached for next time.
            EagerBuildWithoutChanges("Build #3");
        }

        [Fact]
        public void IncrementalBuildCopiesContentWhenOutputsDeleted()
        {
            SetupTestState();
            EagerCleanBuild("Build #1");

            Paths buildPaths = GetBuildPaths();
            File.Delete(buildPaths.CopyOfSourceFile1Path);
            File.Delete(buildPaths.FinalOutputPath);

            Configuration.Schedule.EnableLazyOutputMaterialization = false;

            BuildCounters counters = Build("Build #2");
            counters.VerifyNumberOfPipsExecuted(0);
            counters.VerifyNumberOfProcessPipsCached(TotalPips);
            counters.VerifyNumberOfCachedOutputsUpToDate(0);
            counters.VerifyNumberOfCachedOutputsCopied(TotalPipOutputs);

            // The next build should know that outputs are up to date, despite them having been re-deployed rather than re-computed last time.
            EagerBuildWithoutChanges("Build #3");
        }

        [Fact]
        public void IncrementalBuildCopiesContentWhenOutputsDeletedWithLazyOutputMaterialization()
        {
            SetupTestState();
            Configuration.Engine.DefaultFilter = @"output='" + Path.Combine(Configuration.Layout.ObjectDirectory.ToString(PathTable), "combined") + Path.DirectorySeparatorChar + "*'";
            EagerCleanBuild("Build #1");

            Paths buildPaths = GetBuildPaths();
            File.Delete(buildPaths.CopyOfSourceFile1Path);
            File.Delete(buildPaths.FinalOutputPath);

            BuildCounters counters = Build("Build #2");
            counters.VerifyNumberOfPipsExecuted(0);
            counters.VerifyNumberOfProcessPipsCached(TotalPips);
            counters.VerifyNumberOfCachedOutputsUpToDate(0);

            // Although CopyOfSourceFile1Path is deleted, since output materialization is lazy, the file is not materialized.
            counters.VerifyNumberOfCachedOutputsCopied(TotalPipOutputs - 1);
        }

        protected override string GetSpecContents()
        {
            var copyCmd = OperatingSystemHelper.IsUnixOS
                ? "-c \" /bin/cat one > obj/one_copy \""
                : @"/d /c type one > obj\\one_copy";
            var combineCmd = OperatingSystemHelper.IsUnixOS
                ? "-c \" /bin/cat obj/one_copy two > obj/combined \""
                : @"/d /c type obj\\one_copy > obj\\combined &&; type two >> obj\\combined";

            return $@"
import {{Artifact, Cmd, Tool, Transformer}} from 'Sdk.Transformers';

{GetExecuteFunction()}

const copyOne = Transformer.execute({{
    tool: {GetOsShellCmdToolDefinition()},
    arguments: [
        Cmd.rawArgument('{copyCmd}'),
    ],
    workingDirectory: d`.`,
    dependencies: [
        f`one`,
    ],
    outputs: [
        p`obj/one_copy`
    ],
}});

const combineCopyAndTwo = Transformer.execute({{
    tool: {GetOsShellCmdToolDefinition()},
    arguments: [
        Cmd.rawArgument('{combineCmd}'),
    ],
    workingDirectory: d`.`,
    dependencies: [
        copyOne.getOutputFiles()[0],
        f`two`,
    ],
    outputs: [
        p`obj/combined`
    ],
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
                       SourceFile1Path = Path.Combine(sourceDirectoryPath, "one"),
                       SourceFile2Path = Path.Combine(sourceDirectoryPath, "two"),

                       // Outputs follow.
                       CopyOfSourceFile1Path = Path.Combine(objectDirectoryPath, "one_copy"),
                       FinalOutputPath = Path.Combine(objectDirectoryPath, "combined"),
                   };
        }

        protected override void WriteInitialSources()
        {
            AddFile("one", SourceFile1Contents);
            AddFile("two", SourceFile2Contents);
        }

        protected override void VerifyOutputsAfterBuild(IConfiguration config, PathTable pathTable)
        {
            Paths buildPaths = GetBuildPaths();
            XAssert.AreEqual(
                File.ReadAllText(buildPaths.SourceFile1Path) + File.ReadAllText(buildPaths.SourceFile2Path),
                File.ReadAllText(buildPaths.FinalOutputPath));
        }

        public sealed class Paths
        {
            public string CopyOfSourceFile1Path;
            public string FinalOutputPath;
            public string SourceFile1Path;
            public string SourceFile2Path;
        }
    }
}
