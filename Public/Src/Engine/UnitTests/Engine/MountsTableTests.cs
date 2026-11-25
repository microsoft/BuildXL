// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using BuildXL.Engine;
using BuildXL.Engine.Tracing;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Engine
{
    public sealed class MountsTableTests : XunitBuildXLTest
    {
        public MountsTableTests(ITestOutputHelper output)
            : base(output)
        {
            RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
        }

        [Theory]
        [InlineData("Mount", false)]
        [InlineData("mount", false)] // Case-only name difference.
        [InlineData("Mount", true)]
        public void DuplicateMountNameAtDifferentRootIsError(string secondName, bool moduleMounts)
        {
            var mountsTable = CreateMountsTable(out var context, out var configPath);
            var firstMount = new Mount
            {
                Name = PathAtom.Create(context.StringTable, "Mount"),
                Path = AbsolutePath.Create(context.PathTable, Path.Combine(TestOutputDirectory, "mount")),
            };
            var secondMount = new Mount
            {
                Name = PathAtom.Create(context.StringTable, secondName),
                Path = AbsolutePath.Create(context.PathTable, Path.Combine(TestOutputDirectory, "other")),
            };

            AddMount(mountsTable, firstMount, moduleMounts, new LocationData(configPath, 3, 5));
            AddMount(mountsTable, secondMount, moduleMounts, new LocationData(configPath, 7, 9));

            Assert.False(mountsTable.CompleteInitialization());
            AssertErrorEventLogged(LogEventId.MountsWithSameName);
            AssertErrorEventLogged(LogEventId.ErrorRelatedLocation);

            string configFile = configPath.ToString(context.PathTable);
            var duplicateMessages = EventListener.GetLogMessagesForEventId((int)LogEventId.MountsWithSameName);
            Assert.Single(duplicateMessages);
            Assert.Contains($"file '{configFile}' on line '3' at column '5'", duplicateMessages[0]);

            var relatedMessages = EventListener.GetLogMessagesForEventId((int)LogEventId.ErrorRelatedLocation);
            Assert.Single(relatedMessages);
            Assert.Contains($"file '{configFile}' on line '7' at column '9'", relatedMessages[0]);
            Assert.Contains($"{configFile}(3,5)", relatedMessages[0]);
        }

        [Theory]
        [InlineData("Second", false, false, false, true)] // Conflicting configuration aliases.
        [InlineData("first", false, false, false, true)] // Conflicting case-only name alias.
        [InlineData("Second", false, false, true, false)] // Equivalent configuration aliases.
        [InlineData("Second", true, false, false, false)] // Configuration overrides a static mount.
        [InlineData("Second", false, true, false, false)] // Module overrides a configuration mount.
        public void SharedMountRootUsesLastRegisteredMount(string secondName, bool firstStatic, bool secondModule, bool sameSettings, bool expectWarning)
        {
            var mountsTable = CreateMountsTable(out var context, out _);
            var mountPath = AbsolutePath.Create(context.PathTable, Path.Combine(TestOutputDirectory, "mount"));
            var firstMount = new Mount
            {
                Name = PathAtom.Create(context.StringTable, "First"),
                Path = mountPath,
                IsStatic = firstStatic,
                IsWritable = false,
            };
            var secondMount = new Mount
            {
                Name = PathAtom.Create(context.StringTable, secondName),
                Path = mountPath,
                IsWritable = !sameSettings,
            };

            AddMount(mountsTable, firstMount, moduleDefined: false);
            AddMount(mountsTable, secondMount, secondModule);

            Assert.True(mountsTable.CompleteInitialization());
            AssertWarningEventLogged(LogEventId.MountsWithSamePath, expectWarning ? 1 : 0);
            var mountInfo = mountsTable.MountPathExpander.GetSemanticPathInfo(mountPath);
            Assert.Equal(secondMount.Name, mountInfo.RootName);
            Assert.Equal(secondMount.IsWritable, mountInfo.IsWritable);
            Assert.Same(secondMount, mountsTable.MountsByName[secondName]);
        }

        private MountsTable CreateMountsTable(out BuildXLContext context, out AbsolutePath configPath)
        {
            context = BuildXLContext.CreateInstanceForTesting();
            configPath = AbsolutePath.Create(context.PathTable, Path.Combine(TestOutputDirectory, "config.dsc"));
            var configuration = new CommandLineConfiguration
            {
                Startup = new StartupConfiguration
                {
                    ConfigFile = configPath,
                },
            };

            BuildXLEngine.PopulateLoggingAndLayoutConfiguration(configuration, context.PathTable, bxlExeLocation: null, inTestMode: true);
            return MountsTable.CreateAndRegister(LoggingContext, context, configuration, new Dictionary<string, string>());
        }

        private static void AddMount(MountsTable mountsTable, Mount mount, bool moduleDefined, LocationData? location = null)
        {
            if (moduleDefined)
            {
                mountsTable.AddResolvedModuleDefinedMount(mount, location);
            }
            else
            {
                mountsTable.AddResolvedMount(mount, location);
            }
        }
    }
}
