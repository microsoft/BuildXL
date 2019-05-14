// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Engine;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;


namespace Test.BuildXL.Engine
{
    public class SharedOpaqueEngineTests : BaseEngineTest, IDisposable
    {
        private TestCache m_testCache = new TestCache();
        private CacheInitializer m_cacheInitializer;

        public SharedOpaqueEngineTests(ITestOutputHelper output)
            : base(output)
        {
            // These tests validate the right ACLs are set on particular files. We need the real cache for that.
            m_cacheInitializer = GetRealCacheInitializerForTests();
            ConfigureCache(m_cacheInitializer);
            Configuration.Schedule.DisableProcessRetryOnResourceExhaustion = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (m_cacheInitializer != null)
            {
                var closeResult = m_cacheInitializer.Close();
                if (!closeResult.Succeeded)
                {
                    throw new BuildXLException("Unable to close the cache session: " + closeResult.Failure.DescribeIncludingInnerFailures());
                }
                m_cacheInitializer.Dispose();
                m_cacheInitializer = null;
            }

            base.Dispose(disposing);
        }

        [Fact]
        public void OutputsUnderSharedOpaqueAreSelectivelyScrubbed()
        {
            var file = X("out/MyFile.txt");
            var spec0 = ProduceFileUnderSharedOpaque(file);
            AddModule("Module0", ("spec0.dsc", spec0), placeInRoot: true);

            RunEngine(rememberAllChangedTrackedInputs: true);

            var objDir = Configuration.Layout.ObjectDirectory.ToString(Context.PathTable);

            // Make sure the file was produced
            Assert.True(File.Exists(Path.Combine(objDir, file)));

            // Add an out-of-build file under the same shared opaque directory
            var outOfBuildFile = X("out/OutOfBuildFile.txt");
            File.WriteAllText(Path.Combine(objDir, outOfBuildFile), "Some content");

            // Change the spec so now other file gets produced under the shared opaque
            var anotherFile = X("out/MyOtherFile.txt");
            spec0 = ProduceFileUnderSharedOpaque(anotherFile);

            // Overrite the spec with the new content and run the engine again
            File.WriteAllText(Path.Combine(Configuration.Layout.SourceDirectory.ToString(Context.PathTable), "spec0.dsc"), spec0);

            RunEngine(rememberAllChangedTrackedInputs: true);

            IgnoreWarnings();
            // Make sure the new output is produced
            Assert.True(File.Exists(Path.Combine(objDir, anotherFile)));
            // Make sure the old output is deleted
            Assert.False(File.Exists(Path.Combine(objDir, file)));
            // Make sure the out-of-build file is preserved
            Assert.True(File.Exists(Path.Combine(objDir, outOfBuildFile)));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void UnsafeEmptyDirectoriesUnderSharedOpaqueAreNotScrubbedWhenDisabled(bool disableEmptyDirectoryScrubbing)
        {
            // The unsafe option should be off by default
            Assert.False(Configuration.Schedule.UnsafeDisableSharedOpaqueEmptyDirectoryScrubbing);

            // Create a directory that is not part of the spec
            var objDir = Configuration.Layout.ObjectDirectory.ToString(Context.PathTable);
            var untrackedDirectoryPath = Path.Combine(objDir, X("out/subdir/untracked/"));

            var file = X("out/subdir/MyFile.txt");
            var spec0 = ProduceFileUnderSharedOpaque(file);
            AddModule("Module0", ("spec0.dsc", spec0), placeInRoot: true);

            Assert.False(Directory.Exists(untrackedDirectoryPath));
            Directory.CreateDirectory(untrackedDirectoryPath);
            Configuration.Schedule.UnsafeDisableSharedOpaqueEmptyDirectoryScrubbing = disableEmptyDirectoryScrubbing;
            RunEngine(expectSuccess: true);

            // When the option is disabled, the untracked directory should be removed.
            Assert.Equal(Directory.Exists(untrackedDirectoryPath), disableEmptyDirectoryScrubbing);

            Configuration.Schedule.UnsafeDisableSharedOpaqueEmptyDirectoryScrubbing = false;
        }

        [Fact]
        public void SharedOpaqueOutputsAreScrubbedRegardlessOfDirectoriesKnownToTheGraph()
        {
            var file = X("out/subdir1/subdir2/MyFile.txt");
            var spec0 = ProduceFileUnderSharedOpaque(file);
            AddModule("Module0", ("spec0.dsc", spec0), placeInRoot: true);

            RunEngine(rememberAllChangedTrackedInputs: true);

            var objDir = Configuration.Layout.ObjectDirectory.ToString(Context.PathTable);

            // Make sure the file was produced
            Assert.True(File.Exists(Path.Combine(objDir, file)));

            // Change the spec so now another file is produced and the nested directory where the old output was is 
            // declared as an input to the pip (so therefore becomes part of the graph)
            var anotherFile = X("out/MyOtherFile.txt");
            spec0 = ProduceFileUnderSharedOpaque(anotherFile, dependencies: "f`out/subdir1/subdir2`");

            // Overrite the spec with the new content and run the engine again
            File.WriteAllText(Path.Combine(Configuration.Layout.SourceDirectory.ToString(Context.PathTable), "spec0.dsc"), spec0);

            RunEngine(rememberAllChangedTrackedInputs: true);

            IgnoreWarnings();
            // Make sure the new output is produced
            Assert.True(File.Exists(Path.Combine(objDir, anotherFile)));
            // Make sure the old output is deleted
            Assert.False(File.Exists(Path.Combine(objDir, file)));
        }

        [Fact]
        public void OutputsUnderSharedOpaqueInSubdirAreScrubbed()
        {
            var file = X("out/subdir/MyFile.txt");
            var spec0 = ProduceFileUnderSharedOpaque(file);
            AddModule("Module0", ("spec0.dsc", spec0), placeInRoot: true);

            RunEngine(rememberAllChangedTrackedInputs: true);

            var objDir = Configuration.Layout.ObjectDirectory.ToString(Context.PathTable);

            // Make sure the file was produced
            Assert.True(File.Exists(Path.Combine(objDir, file)));

            // Change the spec so now another file is produced
            var anotherFile = X("out/MyOtherFile.txt");
            spec0 = ProduceFileUnderSharedOpaque(anotherFile);

            // Overrite the spec with the new content and run the engine again
            File.WriteAllText(Path.Combine(Configuration.Layout.SourceDirectory.ToString(Context.PathTable), "spec0.dsc"), spec0);

            RunEngine(rememberAllChangedTrackedInputs: true);

            IgnoreWarnings();
            // Make sure the new output is produced
            Assert.True(File.Exists(Path.Combine(objDir, anotherFile)));
            // Make sure the old output is deleted
            Assert.False(File.Exists(Path.Combine(objDir, file)));
        }

        [Fact]
        public void OutputsUnderSharedOpaqueHaveAWellKnownCreationTimeEvenOnCacheReplay()
        {
            var file = X("out/SharedOpaqueOutput.txt");
            var spec0 = ProduceFileUnderSharedOpaque(file);
            AddModule("Module0", ("spec0.dsc", spec0), placeInRoot: true);

            RunEngine(rememberAllChangedTrackedInputs: true);

            var objDir = Configuration.Layout.ObjectDirectory.ToString(Context.PathTable);

            var producedFile = Path.Combine(objDir, file);
            // Make sure the file was produced
            Assert.True(File.Exists(producedFile));

            // And that it has a well-known creation time
            XAssert.AreEqual(WellKnownTimestamps.OutputInSharedOpaqueTimestamp, FileUtilities.GetFileTimestamps(producedFile).CreationTime);

            File.Delete(producedFile);

            // Replay from cache this time
            RunEngine(rememberAllChangedTrackedInputs: true);

            IgnoreWarnings();
            // Make sure this is a cache replay
            AssertVerboseEventLogged(EventId.ProcessPipCacheHit);
            // And check the timestamp again
            XAssert.AreEqual(WellKnownTimestamps.OutputInSharedOpaqueTimestamp, FileUtilities.GetFileTimestamps(producedFile).CreationTime);
        }

        [Fact]
        public void StaticOutputBecomingASharedOpaqueOutputHasWellKnownCreationTime()
        {
            var file = X("out/MyFile.txt");

            var spec0 = ProduceFileStatically(file);
            AddModule("Module0", ("spec0.dsc", spec0), placeInRoot: true);

            RunEngine(rememberAllChangedTrackedInputs: true);

            var objDir = Configuration.Layout.ObjectDirectory.ToString(Context.PathTable);

            var producedFile = Path.Combine(objDir, file);
            // Make sure the file was produced
            Assert.True(File.Exists(producedFile));

            // Since this is a statically declared file, the creation timestamp should not be the one used for shared opaques
            XAssert.AreNotEqual(WellKnownTimestamps.OutputInSharedOpaqueTimestamp, FileUtilities.GetFileTimestamps(producedFile).CreationTime);

            // Delete the created file (since scrubbing is not on for this test, we have to simulate it)
            File.Delete(producedFile);

            // Overrite the spec so now the same file is generated as a shared opaque output
            spec0 = ProduceFileUnderSharedOpaque(file);
            File.WriteAllText(Path.Combine(Configuration.Layout.SourceDirectory.ToString(Context.PathTable), "spec0.dsc"), spec0);

            // Run the pip
            RunEngine(rememberAllChangedTrackedInputs: true);

            // Check the timestamp is the right one
            XAssert.AreEqual(WellKnownTimestamps.OutputInSharedOpaqueTimestamp, FileUtilities.GetFileTimestamps(producedFile).CreationTime);

            // Delete the file
            File.Delete(producedFile);
            // Replay from cache. Since the file content is unchanged, the cache should have a blob
            // corresponding to the first pip, where the file was a statically declared output
            RunEngine(rememberAllChangedTrackedInputs: true);

            IgnoreWarnings();
            // Make sure this is a cache replay
            AssertVerboseEventLogged(EventId.ProcessPipCacheHit);
            // Check the timestamp is the right one now
            XAssert.AreEqual(WellKnownTimestamps.OutputInSharedOpaqueTimestamp, FileUtilities.GetFileTimestamps(producedFile).CreationTime);
        }

        [Fact]
        public void SharedOpaqueOutputsOnFailingPipHaveWellKnownCreationTime()
        {
            var file = X("out/MyFile.txt");
            var objDir = Configuration.Layout.ObjectDirectory.ToString(Context.PathTable);
            var producedFile = Path.Combine(objDir, file);

            var spec0 = ProduceFileUnderSharedOpaque(file, failOnExit: true);
            AddModule("Module0", ("spec0.dsc", spec0), placeInRoot: true);

            // Run the pip
            RunEngine(rememberAllChangedTrackedInputs: true, expectSuccess: false);

            AssertErrorEventLogged(EventId.PipProcessError);

            // Check the timestamp is the right one
            XAssert.AreEqual(WellKnownTimestamps.OutputInSharedOpaqueTimestamp, FileUtilities.GetFileTimestamps(producedFile).CreationTime);
        }

        private string ProduceFileUnderSharedOpaque(string file, bool failOnExit = false, string dependencies = "") => ProduceFileUnderDirectory(file, isDynamic: true, failOnExit, dependencies);
        private string ProduceFileStatically(string file, bool failOnExit = false) => ProduceFileUnderDirectory(file, isDynamic: false, failOnExit);

        private string ProduceFileUnderDirectory(string file, bool isDynamic, bool failOnExit, string dependencies = "")
        {
            var shellCommand = OperatingSystemHelper.IsUnixOS
                ? "-c 'echo hi" // note we are opening single quotes in this case
                : "/C echo hi";

            var artifactKind = isDynamic
                ? "none"
                : "output";
            var outputs = isDynamic
                ? "[{ kind: 'shared', directory: d`${objDir}/${outFile.parent}` }]"
                : "[]"; 

            string FailOnExitIfNeeded(bool shouldFail)
            {
                var result = string.Empty;
                if (shouldFail)
                {
                    result += ", { value: '&&' }," +
                              "{ value: 'exit' }," +
                              "{ value: '1' }";
                }

                // And we are closing the single quote here
                if (OperatingSystemHelper.IsUnixOS)
                {
                    result += @", {value : ""'""}";
                }

                return result;
            }

            return $@"
import {{Transformer}} from 'Sdk.Transformers';

const objDir = d`${{Context.getMount('ObjectRoot').path}}`;

const outFile = r`{file}`;

{GetExecuteFunction()}

const result = execute({{
    tool: {GetOsShellCmdToolDefinition()},
    workingDirectory: d`.`,
    arguments: [
        {{ value: {{ value: ""{shellCommand}"", kind: ArgumentKind.rawText }}}},
        {{ value: '>' }},
        {{ value: {{ path: p`${{objDir}}/${{outFile}}`, kind: ArtifactKind.{artifactKind} }}}}
        {FailOnExitIfNeeded(failOnExit)}
        ],
    outputs: {outputs},
    dependencies: [{dependencies}],
}});";
        }
    }
}
