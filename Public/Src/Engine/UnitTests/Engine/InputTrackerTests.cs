// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Text;
using BuildXL.Engine;
using BuildXL.Storage;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

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
