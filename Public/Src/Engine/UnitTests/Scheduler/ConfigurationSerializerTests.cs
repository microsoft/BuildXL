// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BuildXL.Scheduler;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

#nullable enable

namespace Test.BuildXL.Scheduler
{
#if NET6_0_OR_GREATER

    public class ConfigurationSerializerTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public ConfigurationSerializerTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task TestSuccessfulSerialization()
        {
            var pathTable = new PathTable();
            var config = new ConfigurationImpl();

            // Serialization returns a very, very long string even without indentation and with removed paths,
            // so it's not viable to hardcode the expected value here. Instead, we just set a few values and
            // check that the json contains the same ones.
            config.DisableInBoxSdkSourceResolver = true;
            config.Engine.ScanChangeJournalTimeLimitInSec = 42;
            config.Cache.CacheSessionName = "test";

            var node = await SerializeToJsonNodeAsync(config, pathTable, indent: true, includePaths: false, ignoreNulls: false);

            XAssert.AreEqual(true, node["DisableInBoxSdkSourceResolver"]!.GetValue<bool>());
            XAssert.AreEqual(42, node["Engine"]!["ScanChangeJournalTimeLimitInSec"]!.GetValue<int>());
            XAssert.AreEqual("test", node["Cache"]!["CacheSessionName"]!.GetValue<string>());
        }

        [Fact]
        public async Task TestIndentationAndNullIgnore()
        {
            var pathTable = new PathTable();
            var config = new ConfigurationForTests();

            var result = await SerializeToJsonStringAsync(config, pathTable, indent: false, includePaths: false, ignoreNulls: true);
            XAssert.AreEqual("{}", result);

            config.IntValue = 1;
            result = await SerializeToJsonStringAsync(config, pathTable, indent: false, includePaths: false, ignoreNulls: true);
            XAssert.AreEqual(@"{""IntValue"":1}", result);

            result = await SerializeToJsonStringAsync(config, pathTable, indent: true, includePaths: false, ignoreNulls: true);
            string expected =
@"{
  ""IntValue"": 1
}";
            if (!OperatingSystemHelper.IsWindowsOS)
            {
                // Line endings are different on Linux, so we need to fix the expected value.
                expected = expected.ReplaceLineEndings();
            }

            XAssert.AreEqual(expected, result);
        }

        [Fact]
        public async Task TestEnumsSerializedAsStrings()
        {
            var pathTable = new PathTable();
            var config = new ConfigurationImpl();
            config.Engine.Phase = EnginePhases.Schedule;

            var node = await SerializeToJsonNodeAsync(config, pathTable, indent: false, includePaths: false, ignoreNulls: true);

            XAssert.AreEqual("Schedule", node["Engine"]!["Phase"]!.GetValue<string>());
        }

        [Fact]
        public async Task TestValueTupleSerialization()
        {
            var pathTable = new PathTable();
            var config = new ConfigurationImpl();
            config.Logging.CustomLog.Add(AbsolutePath.Invalid, (new List<int>(), null));
            var node = await SerializeToJsonNodeAsync(config, pathTable, indent: false, includePaths: false, ignoreNulls: true);
            // ignoreNulls does not affect EventLevel even though it's a nullable type.
            XAssert.AreEqual(@"{""."":{""EventIds"":[],""EventLevel"":null}}", node["Logging"]!["CustomLog"]!.ToJsonString());

            config = new ConfigurationImpl();
            config.Logging.CustomLog.Add(AbsolutePath.Invalid, (new List<int>() { 42 }, EventLevel.Error));
            node = await SerializeToJsonNodeAsync(config, pathTable, indent: false, includePaths: false, ignoreNulls: true);
            XAssert.AreEqual(@"{""."":{""EventIds"":[42],""EventLevel"":""Error""}}", node["Logging"]!["CustomLog"]!.ToJsonString());
        }

        [Fact]
        public async Task TestPathSerialization()
        {
            var pathTable = new PathTable();
            var path = X("/X/abc/foo.bar");
            var absolutePath = AbsolutePath.Create(pathTable, path);
            var fileName = absolutePath.GetName(pathTable);
            var config = new ConfigurationImpl();

            // Check that absolute paths are replaced.
            config.Layout.PrimaryConfigFile = absolutePath;
            var node = await SerializeToJsonNodeAsync(config, pathTable, indent: false, includePaths: false, ignoreNulls: true);
            XAssert.AreEqual(".", node["Layout"]!["PrimaryConfigFile"]!.GetValue<string>());

            // Invalid paths are replaced as well.
            config.Layout.PrimaryConfigFile = AbsolutePath.Invalid;
            node = await SerializeToJsonNodeAsync(config, pathTable, indent: false, includePaths: false, ignoreNulls: true);
            XAssert.AreEqual(".", node["Layout"]!["PrimaryConfigFile"]!.GetValue<string>());

            // AbsolutePath is rendered as a string.
            config.Layout.PrimaryConfigFile = absolutePath;
            node = await SerializeToJsonNodeAsync(config, pathTable, indent: false, includePaths: true, ignoreNulls: true);
            XAssert.AreEqual(path, node["Layout"]!["PrimaryConfigFile"]!.GetValue<string>());

            // Subst paths are translated.
            var target = X("/B/");
            var source = X("/X/abc");
            var translatablePath = X("/B/foo.bar");
            var translatableAbsolutePath = AbsolutePath.Create(pathTable, translatablePath);
            XAssert.IsTrue(PathTranslator.CreateIfEnabled(target, source, out var translator), "Failed to create PathTranslator");
            config.Layout.PrimaryConfigFile = translatableAbsolutePath;
            node = await SerializeToJsonNodeAsync(config, pathTable, translator, indent: false, includePaths: true, ignoreNulls: true);
            XAssert.AreEqual(path, node["Layout"]!["PrimaryConfigFile"]!.GetValue<string>());

            // PathAtoms are not affected by includePaths option.
            config.Ide.SolutionName = PathAtom.Invalid;
            node = await SerializeToJsonNodeAsync(config, pathTable, indent: false, includePaths: false, ignoreNulls: true);
            XAssert.AreEqual("{Invalid}", node["Ide"]!["SolutionName"]!.GetValue<string>());

            config.Ide.SolutionName = fileName;
            node = await SerializeToJsonNodeAsync(config, pathTable, indent: false, includePaths: false, ignoreNulls: true);
            XAssert.AreEqual("foo.bar", node["Ide"]!["SolutionName"]!.GetValue<string>());
        }

        [Fact]
        public async Task TestFileAccessAllowListSerialization()
        {
            var config = new ConfigurationImpl();
            var pathTable = new PathTable();
            var path = X("/X/abc/foo.bar");
            var absolutePath = AbsolutePath.Create(pathTable, path);
            var fileName = absolutePath.GetName(pathTable);
            LocationData ld = new LocationData(absolutePath, 1, 2);

            config.FileAccessAllowList = [new FileAccessAllowlistEntry() { Name = "Test", Location = LocationData.Invalid, ToolPath = new DiscriminatingUnion<FileArtifact, PathAtom>(FileArtifact.CreateSourceFile(absolutePath)) }];
            var node = await SerializeToJsonNodeAsync(config, pathTable, indent: false, includePaths: true, ignoreNulls: true);
            var array = node["FileAccessAllowList"]!.AsArray()!;
            XAssert.AreEqual(1, array.Count);
            XAssert.AreEqual("Test", array[0]!["Name"]!.GetValue<string>());
#if NET8_0_OR_GREATER
            XAssert.AreEqual("{Invalid}", array[0]!["Location"]!.GetValue<string>());
#else
            // System.Text.Json only supports serialization of properties in interface hierarchies starting with .Net8
            // (see: https://github.com/dotnet/runtime/issues/41749)
            // Location is a part of the base interface, i.e., config.FileAccessAllowList <- IFileAccessAllowlistEntry <- ITrackedValue.Location,
            // so it won't be serialized when BuildXL was built under Net6 or Net7.
            XAssert.IsNull(array[0]!["Location"]);
#endif
            XAssert.AreEqual(path, array[0]!["ToolPath"]!.GetValue<string>());

            config.FileAccessAllowList = [new FileAccessAllowlistEntry() { Name = "Test2", Location = LocationData.Create(absolutePath, 1, 2), ToolPath = new DiscriminatingUnion<FileArtifact, PathAtom>(fileName) }];
            node = await SerializeToJsonNodeAsync(config, pathTable, indent: false, includePaths: true, ignoreNulls: true);
            array = node["FileAccessAllowList"]!.AsArray()!;
            XAssert.AreEqual(1, array.Count);
            XAssert.AreEqual("Test2", array[0]!["Name"]!.GetValue<string>());
#if NET8_0_OR_GREATER
            XAssert.AreEqual($"{path} (1, 2)", array[0]!["Location"]!.GetValue<string>());
#endif
            XAssert.AreEqual("foo.bar", array[0]!["ToolPath"]!.GetValue<string>());
        }

        private async Task<string> SerializeToJsonStringAsync(IConfiguration config, PathTable pathTable, bool indent, bool includePaths, bool ignoreNulls)
        {
            using var ms = new MemoryStream();
            var possibleSuccess = await config.SerializeToStreamAsync(ms, pathTable, pathTranslator: null, indent, includePaths, ignoreNulls);
            XAssert.IsTrue(possibleSuccess.Succeeded);
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private Task<System.Text.Json.Nodes.JsonNode> SerializeToJsonNodeAsync(IConfiguration config, PathTable pathTable, bool indent, bool includePaths, bool ignoreNulls)
            => SerializeToJsonNodeAsync(config, pathTable, pathTranslator: null, indent, includePaths, ignoreNulls);

        private async Task<System.Text.Json.Nodes.JsonNode> SerializeToJsonNodeAsync(IConfiguration config, PathTable pathTable, PathTranslator? pathTranslator, bool indent, bool includePaths, bool ignoreNulls)
        {
            using var ms = new MemoryStream();
            var possibleSuccess = await config.SerializeToStreamAsync(ms, pathTable, pathTranslator, indent, includePaths, ignoreNulls);
            XAssert.IsTrue(possibleSuccess.Succeeded);

            ms.Seek(0, SeekOrigin.Begin);
            return JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonNode>(ms)!;
        }

        /// <summary>
        /// This is essentially <see cref="ConfigurationImpl"/> with an extra field and all its original members marked with JsonIgnore.
        /// We need this, so we can test indent and ignoreNulls args.
        /// </summary>
        private class ConfigurationForTests : IConfiguration
        {
            public int? IntValue { get; set; }

            #region All IConfiguration members are excluded from serialization

            [JsonIgnore]
            public IQualifierConfiguration Qualifiers => throw new System.NotImplementedException();

            [JsonIgnore]
            public IReadOnlyList<IResolverSettings> Resolvers => throw new System.NotImplementedException();

            [JsonIgnore]
            public IReadOnlyList<string> AllowedEnvironmentVariables => throw new System.NotImplementedException();

            [JsonIgnore]
            public ILayoutConfiguration Layout => throw new System.NotImplementedException();

            [JsonIgnore]
            public IEngineConfiguration Engine => throw new System.NotImplementedException();

            [JsonIgnore]
            public IScheduleConfiguration Schedule => throw new System.NotImplementedException();

            [JsonIgnore]
            public ISandboxConfiguration Sandbox => throw new System.NotImplementedException();

            [JsonIgnore]
            public ICacheConfiguration Cache => throw new System.NotImplementedException();

            [JsonIgnore]
            public ILoggingConfiguration Logging => throw new System.NotImplementedException();

            [JsonIgnore]
            public IExportConfiguration Export => throw new System.NotImplementedException();

            [JsonIgnore]
            public IExperimentalConfiguration Experiment => throw new System.NotImplementedException();

            [JsonIgnore]
            public IDistributionConfiguration Distribution => throw new System.NotImplementedException();

            [JsonIgnore]
            public IReadOnlyList<AbsolutePath> Projects => throw new System.NotImplementedException();

            [JsonIgnore]
            public IReadOnlyList<AbsolutePath> Packages => throw new System.NotImplementedException();

            [JsonIgnore]
            public IReadOnlyList<AbsolutePath> Modules => throw new System.NotImplementedException();

            [JsonIgnore]
            public bool? DisableDefaultSourceResolver => throw new System.NotImplementedException();

            [JsonIgnore]
            public bool? DisableInBoxSdkSourceResolver => throw new System.NotImplementedException();

            [JsonIgnore]
            public IFrontEndConfiguration FrontEnd => throw new System.NotImplementedException();

            [JsonIgnore]
            public IReadOnlyList<string> CommandLineEnabledUnsafeOptions => throw new System.NotImplementedException();

            [JsonIgnore]
            public IIdeConfiguration Ide => throw new System.NotImplementedException();

            [JsonIgnore]
            public bool? InCloudBuild => throw new System.NotImplementedException();

            [JsonIgnore]
            public bool Interactive => throw new System.NotImplementedException();

            [JsonIgnore]
            public IResolverDefaults ResolverDefaults => throw new System.NotImplementedException();

            [JsonIgnore]
            public IReadOnlyDictionary<ModuleId, IModuleConfiguration> ModulePolicies => throw new System.NotImplementedException();

            [JsonIgnore]
            public IReadOnlyList<RelativePath> SearchPathEnumerationTools => throw new System.NotImplementedException();

            [JsonIgnore]
            public IReadOnlyList<RelativePath> IncrementalTools => throw new System.NotImplementedException();

            [JsonIgnore]
            public ModuleId ModuleId => throw new System.NotImplementedException();

            [JsonIgnore]
            public string Name => throw new System.NotImplementedException();

            [JsonIgnore]
            public IReadOnlyList<IFileAccessAllowlistEntry> FileAccessAllowList => throw new System.NotImplementedException();

            [JsonIgnore]
            public IReadOnlyList<IFileAccessAllowlistEntry> CacheableFileAccessAllowList => throw new System.NotImplementedException();

            [JsonIgnore]
            public IReadOnlyList<IDirectoryMembershipFingerprinterRule> DirectoryMembershipFingerprinterRules => throw new System.NotImplementedException();

            [JsonIgnore]
            public IReadOnlyList<IMount> Mounts => throw new System.NotImplementedException();

            [JsonIgnore]
            public IReadOnlyList<IFileAccessAllowlistEntry> CacheableFileAccessWhitelist => throw new System.NotImplementedException();

            [JsonIgnore]
            public IReadOnlyList<IFileAccessAllowlistEntry> FileAccessWhiteList => throw new System.NotImplementedException();

            [JsonIgnore]
            public LocationData Location => throw new System.NotImplementedException();

            [JsonIgnore]
            public Infra Infra => throw new System.NotImplementedException();

            [JsonIgnore]
            public IReadOnlyList<IReclassificationRuleConfig> GlobalReclassificationRules => throw new System.NotImplementedException();

            public void MarkIConfigurationMembersInvalid() => throw new System.NotImplementedException();

            #endregion
        }
    }
#endif
        }
