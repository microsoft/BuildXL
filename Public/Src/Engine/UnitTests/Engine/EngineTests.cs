// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.Engine;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Engine.Tracing;

namespace Test.BuildXL.EngineTests
{
    public class EngineTests : BaseEngineTest
    {
        public EngineTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestValidateEngineState()
        {
            EngineState stateA = EngineState.CreateDummy(disposed: false);
            EngineState stateB = EngineState.CreateDummy(disposed: false);
            EngineState disposedState = EngineState.CreateDummy(disposed: true);
            EngineState nullState = null;

            // The new state can be null or valid if the previous state is an invalid value.
            ValidateEngineStateCreation(disposedState, nullState, false);
            ValidateEngineStateCreation(disposedState, stateA, false);
            ValidateEngineStateCreation(nullState, nullState, false);
            ValidateEngineStateCreation(nullState, stateA, false);

            // The new state can never be a disposed state
            ValidateEngineStateCreation(nullState, disposedState, true);
            ValidateEngineStateCreation(disposedState, disposedState, true);
            ValidateEngineStateCreation(stateA, disposedState, true);

            // The previous & new states can match
            ValidateEngineStateCreation(stateA, stateA, false);

            // New & Previous cannot both be valid and different
            ValidateEngineStateCreation(stateA, stateB, true);

            // Previous cannot be valid is new is invalid
            ValidateEngineStateCreation(stateA, disposedState, true);
            ValidateEngineStateCreation(stateA, nullState, true);
        }

        private void ValidateEngineStateCreation(EngineState previousState, EngineState newState, bool violation)
        {
            bool contractViolation = false;
            try
            {
                BuildXLEngineResult.Create(true, null, previousState, newState);
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                contractViolation = true;
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

            XAssert.AreEqual(violation, contractViolation);
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
            AssertVerboseEventLogged(EventId.PipTempCleanerThreadSummary);

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
            AssertVerboseEventLogged(EventId.PipTempCleanerThreadSummary);
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
            AssertVerboseEventLogged(EventId.ScrubbingFinished);

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
            AssertVerboseEventLogged(EventId.ScrubbingFile);
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

export const cmdTool: Transformer.ToolDefinition = {
    exe: f`${Environment.getPathValue(""COMSPEC"")}`,
};

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
