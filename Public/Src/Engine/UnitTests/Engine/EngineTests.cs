// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BuildXL.Engine;
using BuildXL.Engine.Recovery;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.Engine;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Engine.Tracing;
using SchedulerLogEventId = BuildXL.Scheduler.Tracing.LogEventId;
using FrontEndLogEventId = BuildXL.FrontEnd.Script.Tracing.LogEventId;
using FrontEndCoreLogEventId = BuildXL.FrontEnd.Core.Tracing.LogEventId;

namespace Test.BuildXL.EngineTests
{
    public class EngineTests : BaseEngineTest
    {
        public EngineTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void TestValidateEngineState(bool success)
        {
            EngineState stateA = EngineState.CreateDummy(disposed: false);
            EngineState stateB = EngineState.CreateDummy(disposed: false);
            EngineState disposedState = EngineState.CreateDummy(disposed: true);
            EngineState nullState = null;

            // The new state can be null or valid if the previous state is an invalid value.
            ValidateEngineStateCreation(success, disposedState, nullState, false);
            ValidateEngineStateCreation(success, disposedState, stateA, false);
            ValidateEngineStateCreation(success, nullState, nullState, false);
            ValidateEngineStateCreation(success, nullState, stateA, false);

            // The new state can never be a disposed state
            ValidateEngineStateCreation(success, nullState, disposedState, true);
            ValidateEngineStateCreation(success, disposedState, disposedState, true);
            ValidateEngineStateCreation(success, stateA, disposedState, true);

            // The previous & new states can match
            ValidateEngineStateCreation(success, stateA, stateA, false);

            // New & Previous cannot both be valid and different
            ValidateEngineStateCreation(success, stateA, stateB, true);

            // Previous cannot be valid is new is invalid
            ValidateEngineStateCreation(success, stateA, disposedState, true);
            ValidateEngineStateCreation(success, stateA, nullState, true);
        }

        [Fact]
        public void TestEngineStateAgainstFailedBuildXLEngineResult()
        {
            EngineState state = EngineState.CreateDummy(disposed: false);
            EngineState disposedState = EngineState.CreateDummy(disposed: true);
            EngineState nullState = null;

            ValidateFailedBuildXLEngineResult(state);
            ValidateFailedBuildXLEngineResult(disposedState);
            ValidateFailedBuildXLEngineResult(nullState);
        }

        private void ValidateEngineStateCreation(bool success, EngineState previousState, EngineState newState, bool violation)
        {
            bool contractViolation = false;
            try
            {
                var result = BuildXLEngineResult.Create(true, null, previousState, newState, shouldDisposePreviousEngineState: false);
                result.DisposePreviousEngineStateIfRequestedAndVerifyEngineStateTransition();
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                contractViolation = true;
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

            XAssert.AreEqual(violation, contractViolation);
        }

        private void ValidateFailedBuildXLEngineResult(EngineState state)
        {
            try
            {
                var result = BuildXLEngineResult.Failed(state);
                result.DisposePreviousEngineStateIfRequestedAndVerifyEngineStateTransition();
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                XAssert.Fail();
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        /// <summary>
        /// Checks that the temp directory for FileUtilities file deletions is automatically set and cleaned by TempCleaner
        /// when the engine runs
        /// </summary>
        [Fact]
        public void TestTempCleanerTempDirectoryCleaned()
        {
            // Run some valid module
            SetupTestData();
            RunEngine();

            // After the engine runs, a temp directory for FileUtilities should have been set
            // It will also be registered for garbage collection with a TempCleaner thread,
            // but there will be nothing to clean
            XAssert.IsTrue(TestHooks.TempCleanerTempDirectory != null);
            XAssert.IsTrue(Directory.Exists(TestHooks.TempCleanerTempDirectory));
            AssertVerboseEventLogged(SchedulerLogEventId.PipTempCleanerThreadSummary);

            // Using the value of the temp directory for FileUtilities,
            // simulate FileUtilities moving a "deleted" file to FileUtilities.TempDirectory
            // The temp directory path is stable build over build as long as the object directory is stable
            string deletedFile = Path.Combine(TestHooks.TempCleanerTempDirectory, "test.txt");
            File.WriteAllText(deletedFile, "asdf");

            // Run again
            RunEngine();

            // The next time the engine runs, TempCleaner should clean up FileUtilities.TempDirectory
            XAssert.IsTrue(Directory.GetFileSystemEntries(TestHooks.TempCleanerTempDirectory).Length == 0);
            XAssert.IsFalse(File.Exists(deletedFile));
            AssertVerboseEventLogged(SchedulerLogEventId.PipTempCleanerThreadSummary);
            XAssert.IsTrue(EventListener.GetLog().Contains("Temp cleaner thread exited with 1 cleaned, 0 remaining and 0 failed temp directories"));
        }

        /// <summary>
        /// Checks that the temp directory for FileUtilities file deletions is NOT scrubbed by the scrubber
        /// This prevents the TempCleaner that cleans the temp directory from getting into a race condition with the scrubber
        /// </summary>
        [Fact]
        public void TestFileUtilitiesTempDirectoryNotScrubbed()
        {
            // Do a run of some valid modules
            // The engine will set and create the path for the FileUtilities temp directory
            // This will also register the directory to be cleaned by TempCleaner
            SetupTestData();
            Configuration.Engine.Scrub = true;
            RunEngine();

            // Expect empty object root to be scrubbed always
            AssertVerboseEventLogged(LogEventId.ScrubbingFinished);

            var objectDirectoryPath = Configuration.Layout.ObjectDirectory.ToString(Context.PathTable);

            // Put a file underneath the object root, which is always scrubbed
            string scrubbedFile = Path.Combine(objectDirectoryPath, "scrubbed");
            File.WriteAllText(scrubbedFile, "asdf");

            // Put a file underneath the FileUtilities temp directory, which is underneath the object root but is not scrubbed
            string unscrubbedFile = Path.Combine(TestHooks.TempCleanerTempDirectory, "unscrubbed");
            File.WriteAllText(unscrubbedFile, "hjkl");

            RunEngine();

            string eventLog = EventListener.GetLog();

            // File underneath object root should be scrubbed
            AssertVerboseEventLogged(LogEventId.ScrubbingFile);
            XAssert.IsTrue(eventLog.Contains($"Scrubber deletes file '{ scrubbedFile }"));
            XAssert.IsFalse(File.Exists(scrubbedFile));

            // Check that no file that starts with FileUtilities temp directory was scrubbed
            // Checking directly for /unscrubbedFile existence is invalid since TempCleaner will have cleaned the directory instead
            XAssert.IsFalse(eventLog.Contains($"Scrubber deletes file '{ TestHooks.TempCleanerTempDirectory }"));
        }

        [Fact]
        public void TestCatastrophicFailureRecovery()
        {
            // Run some valid module
            SetupTestData();
            RunEngine();

            // List the files in the engine cache after a valid run
            var engineCacheDirectory = Configuration.Layout.EngineCacheDirectory.ToString(Context.PathTable);
            var engineCacheFilesList = new List<string>();
            FileUtilities.EnumerateDirectoryEntries(engineCacheDirectory, (file, attributes) =>
            {
                if (!attributes.HasFlag(FileAttributes.Directory))
                {
                    engineCacheFilesList.Add(file);
                }
            });

            var recovery = FailureRecoveryFactory.Create(LoggingContext, Context.PathTable, Configuration);
            // This will trigger the recovery mechanism for unknown catastrophic errors, which is to log and remove the engine state (EngineCache folder)
            XAssert.IsTrue(recovery.TryMarkFailure(new BuildXLException("fake failure"), ExceptionRootCause.Unknown));

            // List the files in the logs directory for corrupt engine cache files
            var logsDirectory = Configuration.Logging.EngineCacheCorruptFilesLogDirectory.ToString(Context.PathTable);
            var logsFilesList = new HashSet<string>();
            FileUtilities.EnumerateDirectoryEntries(logsDirectory, (file, attributes) =>
            {
                logsFilesList.Add(file);
            });

            var childrenCount = Directory.GetFiles(engineCacheDirectory, "*", SearchOption.TopDirectoryOnly).Length;
            var expectedCount = -1;

            // File content table has a special exclusion from the removal policy for performance reasons, but it should still be copied to logs
            // (Unless the file content table doesn't exist in the engine cache, then it doesn't need to exist in the logs)
            var engineCacheFileContentTablePath = Configuration.Layout.FileContentTableFile.ToString(Context.PathTable);
            var fileContentTableFile = Path.GetFileName(engineCacheFileContentTablePath);

            // Make sure file content table was copied to logs
            XAssert.Contains(logsFilesList, fileContentTableFile);
            expectedCount = 1;

            // Make sure file content table file exists in the engine cache directory after recovery
            XAssert.IsTrue(File.Exists(engineCacheFileContentTablePath));

            // Check to make sure the engine cache directory is empty except for maybe the file content table
            XAssert.AreEqual(expectedCount, childrenCount);

            // Check to make sure all the file from the engine cache directory ended up in the logs directory
            foreach (var file in engineCacheFilesList)
            {
                XAssert.Contains(logsFilesList, file);
            }
        }

        [Fact]
        public void TestCleanOnlyArgument()
        {
            var spec0 = SpecWithOpaques();
            AddModule("Module0", ("spec0.dsc", spec0), placeInRoot: true);

            Configuration.Engine.CleanOnly = true;
            Configuration.Schedule.DisableProcessRetryOnResourceExhaustion = true;

            // first run to create all necessary directories leading to obj directory
            RunEngine();

            var objectDirectoryPath = Configuration.Layout.ObjectDirectory.ToString(Context.PathTable);

            var shared = Path.Combine(objectDirectoryPath, "shared");
            Directory.CreateDirectory(shared);
            var fileUnderShared = Path.Combine(shared, "foo.txt");
            File.WriteAllText(fileUnderShared, Guid.NewGuid().ToString());

            var exclusive = Path.Combine(objectDirectoryPath, "exclusive");
            Directory.CreateDirectory(exclusive);
            var fileUnderExclusive = Path.Combine(exclusive, "foo.txt");
            File.WriteAllText(fileUnderExclusive, Guid.NewGuid().ToString());

            RunEngine();

            // the opaque dir should be on disk but they should be empty
            XAssert.IsTrue(Directory.Exists(shared));
            XAssert.IsTrue(Directory.Exists(exclusive));
            XAssert.IsFalse(File.Exists(fileUnderShared));
            XAssert.IsFalse(File.Exists(fileUnderExclusive));
        }

        [Fact]
        public void TestProperLogMessageOnCacheLockAcquisitionFailure()
        {
            var tempDir = Path.Combine(
                OperatingSystemHelper.IsUnixOS ? "/tmp/bxl-temp" : TemporaryDirectory,
                Guid.NewGuid().ToString());
            string cacheDirectory = Path.Combine(tempDir, "cache");

            string cacheConfigJson = $@"{{
    ""MaxCacheSizeInMB"":  1024,
    ""CacheId"":  ""TestCache"",
    ""Assembly"":  ""BuildXL.Cache.MemoizationStoreAdapter"",
    ""CacheLogPath"":  ""[BuildXLSelectedLogPath]"",
    ""Type"": ""BuildXL.Cache.MemoizationStoreAdapter.MemoizationStoreCacheFactory"",
    ""CacheRootPath"":  ""{cacheDirectory.Replace("\\", "\\\\")}"",
    ""UseStreamCAS"":  false,
    ""SingleInstanceTimeoutInSeconds"" : 5
}}";
            AbsolutePath cacheConfigPath = WriteTestCacheConfigToDisk(cacheDirectory, cacheConfigJson);

            var translator = new RootTranslator();
            translator.Seal();

            var possibleFirstCacheInitializer = CacheInitializer.GetCacheInitializationTask(
                LoggingContext,
                Context.PathTable,
                cacheDirectory,
                Path.Combine(TemporaryDirectory, "tmplogdirectory"),
                new CacheConfiguration
                {
                    CacheLogFilePath = AbsolutePath.Create(Context.PathTable, tempDir).Combine(Context.PathTable, "cache.log"),
                    CacheConfigFile = cacheConfigPath
                },
                translator,
                recoveryStatus: false,
                cancellationToken: CancellationToken.None).GetAwaiter().GetResult();

            if (!possibleFirstCacheInitializer.Succeeded)
            {
                AssertTrue(false, "Failed to initialize the cache: " + possibleFirstCacheInitializer.Failure.DescribeIncludingInnerFailures());
            }

            var possibleSecondCacheInitializer = CacheInitializer.GetCacheInitializationTask(
                LoggingContext,
                Context.PathTable,
                cacheDirectory,
                Path.Combine(TemporaryDirectory, "tmplogdirectory"),
                new CacheConfiguration
                {
                    // need a different name for the log file (due to the order in which the things are initialized)
                    CacheLogFilePath = AbsolutePath.Create(Context.PathTable, tempDir).Combine(Context.PathTable, "cache_2.log"),
                    CacheConfigFile = cacheConfigPath,

                },
                translator,
                recoveryStatus: false,
                cancellationToken: CancellationToken.None).GetAwaiter().GetResult();

            // close and dispose the first cache (must be done before the assert block bellow)
            var firstCacheInitializer = possibleFirstCacheInitializer.Result;
            AssertSuccess(firstCacheInitializer.Close());
            firstCacheInitializer.Dispose();

            AssertErrorEventLogged(global::BuildXL.Engine.Tracing.LogEventId.FailedToAcquireDirectoryLock);
            AssertTrue(!possibleSecondCacheInitializer.Succeeded, "Initialization of the second cache should have failed.");
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestRedirectUserProfileDirectory()
        {
            // first run to create all necessary directories leading to obj directory
            SetupTestData();
            RunEngine();

            string currentUserProfile = SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string junctionPath = Path.Combine(Configuration.Layout.ObjectDirectory.ToString(Context.PathTable), "buildXLUserProfile");
            bool specialFolderInitializerWasCalled = false;
            var translatedDirectory = new List<TranslateDirectoryData>();
            var properties = new Dictionary<string, string>();
            var expectedProperties = new Dictionary<string, string>
            {
                { "APPDATA", Path.Combine(junctionPath, "AppData", "Roaming") },
                { "LOCALAPPDATA", Path.Combine(junctionPath, "AppData", "Local") },
                { "USERPROFILE", junctionPath },
                { "USERNAME", "buildXLUserProfile" },
                { "HOMEDRIVE", Path.GetPathRoot(junctionPath).TrimEnd('\\') },
                { "HOMEPATH", junctionPath.Substring(Path.GetPathRoot(junctionPath).TrimEnd('\\').Length) },
                { "INTERNETCACHE", Path.Combine(junctionPath, "AppData", "Local", "Microsoft", "Windows", "INetCache") },
                { "INTERNETHISTORY", Path.Combine(junctionPath, "AppData", "Local", "Microsoft", "Windows", "History") },
                { "INETCOOKIES", Path.Combine(junctionPath, "AppData", "Local", "Microsoft", "Windows", "INetCookies") },
                { "LOCALLOW", Path.Combine(junctionPath, "AppData", "LocalLow") },
            };

            // add the variables to the dictionary, so we can verify that the method overrides the existing values
            foreach (var envVar in expectedProperties.Keys)
            {
                properties.Add(envVar, string.Empty);
            }

            try
            {
                var success = BuildXLEngine.RedirectUserProfileDirectory(
                    Configuration.Layout.ObjectDirectory,
                    translatedDirectory,
                    properties,
                    dict => { specialFolderInitializerWasCalled = true; },
                    true,
                    Context.PathTable,
                    LoggingContext);

                // must have finished successfully
                XAssert.IsTrue(success);

                // verify the env block is properly populated
                XAssert.AreSetsEqual(expectedProperties.Keys, properties.Keys, true);
                XAssert.IsFalse(expectedProperties.Any(kvp => properties[kvp.Key] != kvp.Value));

                XAssert.IsTrue(specialFolderInitializerWasCalled);

                // verify junction
                var openResult = FileUtilities.TryOpenDirectory(junctionPath, FileDesiredAccess.FileReadAttributes, FileShare.ReadWrite, FileFlagsAndAttributes.FileFlagOpenReparsePoint, out var handle);
                XAssert.IsTrue(openResult.Succeeded);

                using (handle)
                {
                    var possibleTarget = FileUtilities.TryGetReparsePointTarget(handle, junctionPath);
                    XAssert.IsTrue(possibleTarget.Succeeded);
                    XAssert.AreEqual(FileSystemWin.NtPathPrefix + currentUserProfile, possibleTarget.Result);
                }

                // verify that we added a new directory translation
                AbsolutePath.TryCreate(Context.PathTable, currentUserProfile, out var fromPath);
                AbsolutePath.TryCreate(Context.PathTable, junctionPath, out var toPath);
                XAssert.IsTrue(translatedDirectory.Count == 1);
                XAssert.IsTrue(translatedDirectory[0].FromPath == fromPath && translatedDirectory[0].ToPath == toPath);
            }
            finally
            {
                // clean the junction after the test
                var possibleProbe = FileUtilities.TryProbePathExistence(junctionPath, false);
                if (possibleProbe.Succeeded && possibleProbe.Result != PathExistence.Nonexistent)
                {
                    // we attempt to delete the junction, but we do not care if we failed to do
                    FileUtilities.TryRemoveDirectory(junctionPath, out int errCode);
                }
            }
        }

        [Fact]
        public void TestGraphCacheMissReason()
        {
            foreach (var missReason in Enum.GetValues(typeof(GraphCacheMissReason)))
            {
                var statistics = new GraphCacheCheckStatistics() { MissReason = (GraphCacheMissReason)missReason };
                // Make sure all miss reasons are covered in the display text here
                Console.WriteLine(statistics.MissMessageForConsole);
            }
        }

        // While this tests succeeds with a debugger attached, it fails when ran through BuildXL on macOS, keep it windows-only for now
        // TODO: Re-enable this for Linux, EmitSpotlightIndexingWarning is not logged. Work Item#1984802
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void TestSpotlightCheck()
        {
            PathTable pt = new PathTable();
            AbsolutePath trailingNoindex = AbsolutePath.Create(pt, Path.Combine(TestRoot, ".noindex"));
            AbsolutePath intermediateNoindex = AbsolutePath.Create(pt, Path.Combine(TestRoot, ".noindex", "subdirectory"));
            BuildXLEngine.CheckArtifactFolersAndEmitNoIndexWarning(pt, LoggingContext, trailingNoindex, intermediateNoindex);
            XAssert.AreEqual(0, EventListener.GetEventCount((int)LogEventId.EmitSpotlightIndexingWarning), "No warning should be logged for either path containing noindex");

            AbsolutePath indexablePath = AbsolutePath.Create(pt, Path.Combine(TestRoot, "subdirectory"));
            BuildXLEngine.CheckArtifactFolersAndEmitNoIndexWarning(pt, LoggingContext, indexablePath);
            AssertWarningEventLogged(LogEventId.EmitSpotlightIndexingWarning);
        }

        /// <summary>
        /// Tests the allow missing specs flag to ensure that the engine doesn't throw an error when an empty module
        /// or spec file is provided.
        /// </summary>
        /// <param name="allowMissingSpecs"> 
        /// Tests with the flag enabled and disabled to ensure that an error is still
        /// thrown when a bad spec is provided without the flag enabled
        /// </param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void VerifyAllowMissingSpecsFlag(bool allowMissingSpecs)
        {
            var missingModuleConfig = Path.Combine(TestRoot, "src", "inexistentModule.config.dsc");
            var missingTestFile = Path.Combine(TestRoot, "inexistentSpec.dsc");
            var config = @"
config({
    resolvers: [{ 
        kind: ""DScript"",
        modules:[f`module.config.dsc`, f`src/inexistentModule.config.dsc`]
    }],
    mounts: [{
        name: a`Src`,
        isReadable: true,
        isWritable: true,
        isScrubbable: false,
        path: p`.`,
        trackSourceFileChanges: true
    }],
    disableDefaultSourceResolver: true
}); ";

            var moduleContents = @"
module({ 
    name: ""Test"", projects: [f`spec.dsc`, f`inexistentSpec.dsc`]
});";
            var specFileContents = @"const test = Debug.writeLine(""test"");";

            SetConfig(config);
            AddFile(Path.Combine(TestRoot, "module.config.dsc"), moduleContents);
            AddFile(Path.Combine(TestRoot, "spec.dsc"), specFileContents);

            Configuration.FrontEnd.AllowMissingSpecs = allowMissingSpecs;

            RunEngine(expectSuccess: allowMissingSpecs);

            if (allowMissingSpecs)
            {
                AssertVerboseEventLogged(FrontEndLogEventId.SourceResolverModuleFilesDoNotExistVerbose, count: 1); // Logged once for src/module.config.dsc
                AssertVerboseEventLogged(FrontEndLogEventId.ModuleProjectFileDoesNotExist, count: 2); // Logged twice, once for src/module.config.dsc and once for test2.dsc
                AssertLogContains(false, missingModuleConfig);
                AssertLogContains(false, missingTestFile);
            }
            else
            {
                AssertErrorEventLogged(FrontEndLogEventId.SourceResolverPackageFilesDoNotExist, count: 1);
            }
        }

        /// <summary>
        /// Tests the allowMissingSpec flag with an inline module definition that has a missing project.
        /// </summary>
        [Fact]
        public void VerifyAllowMissingSpecsFlagWithInlineModule()
        {
            var config = @"
config({
    resolvers: [{ 
        kind: ""DScript"",
        modules: [{moduleName: ""Test"", projects: [f`spec.dsc`, f`inexistentSpec.dsc`]}]
    }],
    mounts: [{
        name: a`Src`,
        isReadable: true,
        isWritable: true,
        isScrubbable: false,
        path: p`.`,
        trackSourceFileChanges: true
    }],
    disableDefaultSourceResolver: true
}); ";
            var specFileContents = @"const test = Debug.writeLine(""test"");";

            SetConfig(config);
            AddFile(Path.Combine(TestRoot, "spec.dsc"), specFileContents);

            Configuration.FrontEnd.AllowMissingSpecs = true;

            RunEngine(expectSuccess: true);
            AssertVerboseEventLogged(FrontEndLogEventId.ModuleProjectFileDoesNotExist, count: 1);
        }

        /// <summary>
        /// Verifies that even when the allow missing spec flag is set, if a module that is missing is referenced by another
        /// an error will still be thrown and the build will fail. There should always be an error regardless of whether
        /// the flag is set or not.
        /// </summary>
        /// <param name="allowMissingSpecs"> 
        /// Tests with the flag enabled and disabled to ensure that an error is always thrown regardless of the flag being enabled
        /// when there is a user error.
        /// </param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void VerifyAllowMissingSpecsWithMissingModuleReference(bool allowMissingSpecs)
        {
            var config = @"
config({
    resolvers: [{ 
        kind: ""DScript"",
        modules: [{moduleName: ""Test"", projects: " + (allowMissingSpecs ? "[f`missingSpec.dsc`]" : "[f`realSpec.dsc`]") + @"}, {moduleName: ""Test2"", projects: [f`realSpec2.dsc`]}]
    }],
    mounts: [{
        name: a`Src`,
        isReadable: true,
        isWritable: true,
        isScrubbable: false,
        path: p`.`,
        trackSourceFileChanges: true
    }],
    disableDefaultSourceResolver: true
}); ";

            var specFile = @" ";
            var spec2File = @"
import * as T from ""Test"";
const y = Debug.writeLine(T.testValue); ";

            SetConfig(config);
            AddFile(Path.Combine(TestRoot, "realSpec.dsc"), specFile);
            AddFile(Path.Combine(TestRoot, "realSpec2.dsc"), spec2File);

            Configuration.FrontEnd.AllowMissingSpecs = allowMissingSpecs;

            RunEngine(expectSuccess: false);

            // allowMissingSpec=true: Property 'testValue' does not exist on type 'typeof Test'.
            // allowMissingSpec=false: Property 'testValue' does not exist on type 'typeof Test'.
            AssertErrorEventLogged(FrontEndCoreLogEventId.CheckerError);
            AssertErrorEventLogged(FrontEndCoreLogEventId.CannotBuildWorkspace); // One or more error occurred during workspace analysis
        }

        private void SetupTestData()
        {
            var spec0 = @"
import {Artifact, Cmd, Tool, Transformer} from 'Sdk.Transformers';

namespace Namespace1.MyValueInTheSameNamespace {
    const subnamespace = 'MyValue';
}

namespace Namespace2 {
    const valueInOtherNamespace = 'MyValue';
}

namespace Namespace1 {
    export const value1 = 'Value from current module which is private';
}

const step1 = Transformer.writeAllLines(
    p`obj/a.txt`,
    [
        Namespace1.value1,
    ]
);
";
            var spec1 = @"
@@public
export const value1 = 'Value from module1';
";

            AddModule("Module0", ("spec0.dsc", spec0), placeInRoot: true);
            AddModule("Module1", ("spec1.dsc", spec1));
        }

        private static string SpecWithOpaques()
        {
            return @"
import {Artifact, Cmd, Tool, Transformer} from 'Sdk.Transformers';

const exclusiveOpaque: Directory = d`obj/exclusive`;
const sharedOpaque: Directory = d`obj/shared`;

export const cmdTool: Transformer.ToolDefinition = {" +
    $"exe: f`{(OperatingSystemHelper.IsUnixOS ? "/bin/sh" : @"${Environment.getPathValue(""COMSPEC"")}")}`"
+ @"};

const pip = Transformer.execute({
    tool: cmdTool,
    workingDirectory: d`.`,
    arguments : [],
    outputs:[
        p`obj/foo.txt`,
		{directory: exclusiveOpaque, kind: ""exclusive""},
        {directory: sharedOpaque, kind: ""shared""},
    ],
});
";
        }

        private static void AssertSuccess<T>(Possible<T, Failure> possible)
        {
            Assert.True(possible.Succeeded);
        }
    }
}
