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
    /// <summary>
    /// Incremental build tests for <see cref="DirectoryArtifact" /> dependencies.
    /// </summary>
    [Trait("Category", "DirectoryArtifactIncrementalBuildTests")]
    public sealed class DirectoryArtifactIncrementalBuildTests : IncrementalBuildTestBase
    {
        private const string HeaderAInitialContents = "Alpha";
        private const string HeaderBInitialContents = "Beta";

        public DirectoryArtifactIncrementalBuildTests(ITestOutputHelper output)
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
        public void DeletingUsedInput()
        {
            SetupTestState();
            EagerCleanBuild("Build #1");
            Paths buildPaths = GetBuildPaths();
            File.Delete(buildPaths.HeaderAPath);
            EagerCleanBuild("Build #2");

            EagerBuildWithoutChanges("Build #3");
        }

        [Fact]
        public void ChangingUnusedInput()
        {
            SetupTestState();
            EagerCleanBuild("Build #1");

            Paths buildPaths = GetBuildPaths();
            File.WriteAllText(buildPaths.HeaderBPath, "Unused");
            EagerBuildWithoutChanges("Build #2");
        }

        [Fact]
        public void SwappingContentsOfUsedInputWithUnused()
        {
            SetupTestState();
            EagerCleanBuild("Build #1");
            Paths buildPaths = GetBuildPaths();
            File.WriteAllText(buildPaths.HeaderAPath, HeaderBInitialContents);
            File.WriteAllText(buildPaths.HeaderBPath, HeaderAInitialContents);
            EagerCleanBuild("Build #2");

            EagerBuildWithoutChanges("Build #3");
        }

        [Fact]
        public void AntiDependencyInvalidated()
        {
            SetupTestState();
            Paths buildPaths = GetBuildPaths();
            File.Delete(buildPaths.HeaderAPath);
            EagerCleanBuild("Build #1");
            File.WriteAllText(buildPaths.HeaderAPath, HeaderAInitialContents);
            EagerCleanBuild("Build #2");

            EagerBuildWithoutChanges("Build #3");
        }

        protected override string GetSpecContents()
        {
            var pickAOrBCmd = OperatingSystemHelper.IsUnixOS
                ? "-c \" if [[ -f inc/a.h ]]; then /bin/cat inc/a.h; else /bin/cat inc/b.h; fi \""
                : @"/d /c (if exist inc\\a.h (type inc\\a.h) else (type inc\\b.h))";
            var useAOrBCmd = OperatingSystemHelper.IsUnixOS
                ? "-c \" if [[ -f obj/inc/absent.h ]]; then /bin/cat obj/inc/absent.h; else /bin/cat obj/inc/A_or_B.h; fi \""
                : @"/d /c (if exist obj\\inc\\absent.h (type obj\\inc\\absent.h) else (type obj\\inc\\A_or_B.h))";

            return $@"
import {{Artifact, Cmd, Tool, Transformer}} from 'Sdk.Transformers';

{GetExecuteFunction()}

const inc = Transformer.sealDirectory(
    d`inc`, 
    [
        f`inc/a.h`,
        f`inc/b.h`,
    ]);

const pickAOrB = execute({{
    tool: {GetOsShellCmdToolDefinition()},
    arguments: [
        Cmd.rawArgument('{pickAOrBCmd}'),
    ],
    workingDirectory: d`.`,
    dependencies: [
        inc,
    ],
    consoleOutput: p`obj/inc/A_or_B.h`,
}});
const pickAOrBFile = pickAOrB.getOutputFile(p`obj/inc/A_or_B.h`);

const outInc = Transformer.sealDirectory(
    d`obj/inc`,
    [
        pickAOrBFile,
    ]);

const useAOrB = execute({{
    tool: {GetOsShellCmdToolDefinition()},
    arguments: [
        Cmd.rawArgument('{useAOrBCmd}'),
    ],
    workingDirectory: d`.`,
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
                       HeaderAPath = Path.Combine(sourceDirectoryPath, X("inc/a.h")),
                       HeaderBPath = Path.Combine(sourceDirectoryPath, X("inc/b.h")),

                       // Outputs follow.
                       SelectedHeaderPath = Path.Combine(objectDirectoryPath, X("inc/A_or_B.h")),
                       FinalOutputPath = Path.Combine(objectDirectoryPath, "final.out"),
                   };
        }

        protected override void WriteInitialSources()
        {
            AddFile(X("inc/a.h"), HeaderAInitialContents);
            AddFile(X("inc/b.h"), HeaderBInitialContents);
        }

        protected override void VerifyOutputsAfterBuild(IConfiguration config, PathTable pathTable)
        {
            string contents;
            Paths buildPaths = GetBuildPaths();
            if (File.Exists(buildPaths.HeaderAPath))
            {
                contents = File.ReadAllText(buildPaths.HeaderAPath);
            }
            else if (File.Exists(buildPaths.HeaderBPath))
            {
                contents = File.ReadAllText(buildPaths.HeaderBPath);
            }
            else
            {
                XAssert.Fail("Either a.h or b.h should be written, as required by the first process.");
                return;
            }

            XAssert.IsTrue(File.Exists(buildPaths.SelectedHeaderPath), "Selected header output missing");
            XAssert.IsTrue(File.Exists(buildPaths.FinalOutputPath), "Final output missing");

            XAssert.AreEqual(contents, File.ReadAllText(buildPaths.SelectedHeaderPath).TrimEnd(), "Incorrect contents for A_or_B.h");
            XAssert.AreEqual(
                contents,
                File.ReadAllText(buildPaths.FinalOutputPath).TrimEnd(),
                "Incorrect contents for final.out (but its only input was correct)");
        }

        private sealed class Paths
        {
            public string HeaderAPath;
            public string HeaderBPath;
            public string SelectedHeaderPath;
            public string FinalOutputPath;
        }
    }
}
