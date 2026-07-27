// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Engine
{
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true)]
    public class InputTrackerTests : TemporaryStorageTestBase
    {
        public InputTrackerTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void RegisterFileAccessOnMissingPathRecordsUnknownContent()
        {
            // A front-end reported a file as accessed, but by the time the input tracker tries to
            // open and hash it, the file cannot be opened (FileNotFound / DirectoryNotFound).
            var loggingContext = new LoggingContext("Test");
            FileContentTable fileContentTable = FileContentTable.CreateStub(loggingContext);
            var graphFingerprint = new GraphFingerprint(CompositeGraphFingerprint.Zero, CompositeGraphFingerprint.Zero);

            InputTracker inputTracker = InputTracker.Create(
                loggingContext,
                fileContentTable,
                JournalState.DisabledJournal,
                graphFingerprint.ExactFingerprint);

            string missingPath = Path.Combine(TemporaryDirectory, "does_not_exist", "also_does_not_exist");

            // Should not throw.
            inputTracker.RegisterFileAccess(missingPath);

            XAssert.IsTrue(
                inputTracker.TryGetHashForKnownInputFile(missingPath, out ContentHash recordedHash),
                "Expected the missing path to be recorded in the input tracker.");
            XAssert.AreEqual(WellKnownContentHashes.UnknownContent, recordedHash);
            XAssert.IsTrue(inputTracker.HasUncapturedInputs, "Expected HasUncapturedInputs to be set so PreviousInputs is not persisted.");
        }

        [Fact]
        public void TestProbeUnsetEnvVar()
        {
            var loggingContext = new LoggingContext("Test");
            BuildXLContext buildXLContext = BuildXLContext.CreateInstanceForTesting();
            string fileTrackerPath = GetFullPath("fileTracker");
            FileContentTable fileContentTable = FileContentTable.CreateStub(loggingContext);
            var graphFingerprint = new GraphFingerprint(CompositeGraphFingerprint.Zero, CompositeGraphFingerprint.Zero);

            InputTracker inputTracker = InputTracker.Create(
                loggingContext,
                fileContentTable,
                JournalState.DisabledJournal,
                graphFingerprint.ExactFingerprint);

            using var stream = new MemoryStream();

            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                inputTracker.WriteToFile(
                    writer,
                    buildXLContext.PathTable,
                    new Dictionary<string, string>(1)
                    {
                        { "UnsetEnvVar", null } // Unset environment variable that got probed.
                    },
                    new Dictionary<string, IMount>(0),
                    fileTrackerPath);
            }

            stream.Position = 0;
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                var configuration = new CommandLineConfiguration()
                {
                    Startup = new StartupConfiguration()
                    {
                        ConfigFile = AbsolutePath.Create(buildXLContext.PathTable, Path.Combine(TemporaryDirectory, "config.dc"))
                    }
                };
                BuildXLEngine.PopulateLoggingAndLayoutConfiguration(configuration, buildXLContext.PathTable, bxlExeLocation: null, inTestMode: true);
                MountsTable mountsTable = MountsTable.CreateAndRegister(loggingContext, buildXLContext, configuration, new Dictionary<string, string>(0));
                mountsTable.CompleteInitialization();
                InputTracker.MatchResult? matchResult = InputTracker.MatchesReader(
                    loggingContext,
                    reader,
                    fileContentTable,
                    JournalState.DisabledJournal,
                    default,
                    fileTrackerPath,
                    BuildParameters.GetFactory().PopulateFromDictionary([]),
                    mountsTable,
                    graphFingerprint,
                    1,
                    configuration,
                    true);

                XAssert.IsTrue(matchResult.HasValue);
                XAssert.IsTrue(matchResult.Value.Matches, $"Match result: {matchResult.Value.MissType}");
            }
        }

        [Fact]
        public void TestProbeUnknownMount()
        {
            var loggingContext = new LoggingContext("Test");
            BuildXLContext buildXLContext = BuildXLContext.CreateInstanceForTesting();
            string fileTrackerPath = GetFullPath("fileTracker");
            FileContentTable fileContentTable = FileContentTable.CreateStub(loggingContext);
            var graphFingerprint = new GraphFingerprint(CompositeGraphFingerprint.Zero, CompositeGraphFingerprint.Zero);

            InputTracker inputTracker = InputTracker.Create(
                loggingContext,
                fileContentTable,
                JournalState.DisabledJournal,
                graphFingerprint.ExactFingerprint);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                inputTracker.WriteToFile(
                    writer,
                    buildXLContext.PathTable,
                    new Dictionary<string, string>(0),
                    new Dictionary<string, IMount>(1)
                    {
                        { "UnknownMount", null } // Unknown mount that got probed.
                    },
                    fileTrackerPath);
            }

            stream.Position = 0;
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                var configuration = new CommandLineConfiguration()
                {
                    Startup = new StartupConfiguration()
                    {
                        ConfigFile = AbsolutePath.Create(buildXLContext.PathTable, Path.Combine(TemporaryDirectory, "config.dc"))
                    }
                };
                BuildXLEngine.PopulateLoggingAndLayoutConfiguration(configuration, buildXLContext.PathTable, bxlExeLocation: null, inTestMode: true);
                MountsTable mountsTable = MountsTable.CreateAndRegister(loggingContext, buildXLContext, configuration, new Dictionary<string, string>(0));
                mountsTable.CompleteInitialization();
                InputTracker.MatchResult? matchResult = InputTracker.MatchesReader(
                    loggingContext,
                    reader,
                    fileContentTable,
                    JournalState.DisabledJournal,
                    default,
                    fileTrackerPath,
                    BuildParameters.GetFactory().PopulateFromDictionary([]),
                    mountsTable,
                    graphFingerprint,
                    1,
                    configuration,
                    true);

                XAssert.IsTrue(matchResult.HasValue);
                XAssert.IsTrue(matchResult.Value.Matches, $"Match result: {matchResult.Value.MissType}");
            }
        }
    }
}
