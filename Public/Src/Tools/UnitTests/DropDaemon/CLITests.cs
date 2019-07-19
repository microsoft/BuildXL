// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.CLI;
using Test.BuildXL.TestUtilities.Xunit;
using Tool.DropDaemon;
using Tool.ServicePipDaemon;
using Xunit;
using Xunit.Abstractions;
using static Tool.DropDaemon.DropDaemon;
using static Tool.ServicePipDaemon.ServicePipDaemon;

namespace Test.Tool.DropDaemon
{
    public sealed class CLITests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        internal ITestOutputHelper Output { get; }

        public CLITests(ITestOutputHelper output)
            : base(output)
        {
            Output = output;
        }

        [Theory]
        [InlineData("start", "")]
        [InlineData("create", "")]
        [InlineData("addfile", "--file xyz.txt --dropPath a/b/c.txt")]
        [InlineData("addfile", "--file xyz.txt")]
        [InlineData("finalize", "")]
        [InlineData("stop", "")]
        public void CloudBuildOfficeTests(string cmdlinePre, string cmdlinePost)
        {
            string jsonContent = @"
{
  ""Name"": ""Office / DropTest / 13685066 / 62260316 - 7764 - 4421 - 9432 - a62d548524c1"",
  ""EnableCloudBuildIntegration"": true,
  ""Service"": ""https://office.artifacts.visualstudio.com/DefaultCollection"",
  ""RetentionDays"": 90
}
";
            var fullCmdLine = ConstructCmdLine(cmdlinePre, jsonContent, cmdlinePost);
            var cliConfig = ParseArgs(fullCmdLine, ignoreInvalidOptions: true);
        }

        [Theory]
        [MemberData(nameof(ValidCommandLinesTestData))]
        public void TestValidCommandLines(string cmdName, IEnumerable<Option> options, IEnumerable<string> optionValues)
        {
            Assert.Equal(options.Count(), optionValues.Count());
            var switchChoices = new IEnumerable<string>[]
            {
                options.Zip(optionValues, (opt, val) => $"--{opt.LongName} {val}"),
                options.Zip(optionValues, (opt, val) => $"-{opt.ShortName} {val}"),
            };
            foreach (var switches in switchChoices)
            {
                var joinedSwitches = string.Join(" ", switches);
                var cmdline = $"{cmdName} {joinedSwitches}";
                var conf = ParseArgs(cmdline);
                Assert.NotNull(conf);
            }
        }

        public static IEnumerable<object[]> ValidCommandLinesTestData()
        {
            yield return new object[] { "start", new Option[] { DropNameOption, DropEndpoint }, new[] { "mydrop", "http://xyz" } };
            yield return new object[] { "start", new Option[] { DropNameOption, DropEndpoint, EnableCloudBuildIntegration }, new[] { "mydrop", "http://xyz", "True" } };
            yield return new object[] { "create", new Option[] { DropNameOption, DropEndpoint }, new[] { "mydrop", "http://xyz" } };
            yield return new object[] { "addfile", new[] { File, DropNameOption }, new[] { @"""c:\x\y.txt""", "mydrop" } };
            yield return new object[] { "addfile", new[] { File }, new[] { @"""c:\x\y.txt""" } };
            yield return new object[] { "addfile", new[] { File, RelativeDropPath }, new[] { @"""c:\x\y.txt""", "a/b/c.txt" } };
            yield return new object[] { "addfile", new[] { File, RelativeDropPath }, new[] { @"""c:\x\y.txt""", @"a\\b\\c.txt" } };
            yield return new object[] { "addfile", new[] { File, RelativeDropPath }, new[] { @"""c:\x\y.txt""", "\"a\\b\\c.txt\"" } };
            yield return new object[] { "finalize", new Option[0], new string[0] };
            yield return new object[] { "finalize", new[] { DropNameOption }, new[] { "mydrop" } };
            yield return new object[] { "stop", new Option[0], new string[0] };
            yield return new object[] { "stop", new[] { DropNameOption }, new[] { "mydrop" } };
            yield return new object[] { "addartifacts", new[] { IpcServerMonikerRequired, Directory, DirectoryId, RelativeDirectoryDropPath }, new[] { "moniker_string", @"c:\dir", "123:1:12345", "/remote/" } };
            yield return new object[] { "addartifacts", new[] { IpcServerMonikerRequired, File, FileId, HashOptional, RelativeDropPath }, new[] { "moniker_string", @"c:\dir\f.txt", "id1", "123:1:12345", "/remote/f.txt" } };
        }

        [Theory]
        [MemberData(nameof(OptionOverrideTestData))]
        public void TestOptionOverride(Option option, string valuePre, string valueJson, string valuePost)
        {
            var pre = valuePre != null
                ? $"--{option.LongName} {valuePre}"
                : string.Empty;
            var json = valueJson != null
                ? ToJsonStr(option.LongName, QuoteStr(valueJson))
                : null;
            var post = valuePost != null
                ? $"--{option.LongName} {valuePost}"
                : null;

            var cmdline = ConstructCmdLine("start --service http://qwe " + pre, json, post);
            var dropConf = CreateDropConfig(ParseArgs(cmdline));
            Assert.NotNull(dropConf);

            var expectedValue = valuePost ?? valueJson ?? valuePre;
            Assert.Equal(expectedValue, dropConf.Name);
        }

        public static IEnumerable<object[]> OptionOverrideTestData()
        {
            yield return new object[] { DropNameOption, "a", "b", "c" };
            yield return new object[] { DropNameOption, "a", "b", null };
            yield return new object[] { DropNameOption, "a", null, "c" };
            yield return new object[] { DropNameOption, null, "b", "c" };
            yield return new object[] { DropNameOption, null, "b", null };
        }

        /// <summary>
        ///     For each command in <see cref="Commands"/> we generate a command line where
        ///     exactly one required option is missing and assert that <see cref="ArgumentException"/>
        ///     is thrown when parsing that command line.
        /// </summary>
        [Theory]
        [MemberData(nameof(ParseArgsRequiredOptionMissingTestData))]
        public void TestParseArgsRequiredOptionMissing(string cmdline)
        {
            var e = Assert.Throws<ArgumentException>(() => ParseArgs(cmdline));
            var errorMessage = "expected parsing of [[ " + cmdline + " ]] to return an error containing words 'required' and 'missing', but it doesn't: " + e.Message;
            Assert.True(e.Message != null, errorMessage);
            Assert.True(e.Message.Contains("required") && e.Message.Contains("missing"), errorMessage);
        }

        public static IEnumerable<object[]> ParseArgsRequiredOptionMissingTestData()
        {
            // TODO: this only iterates over DropDaemon & ServicePipDaemon commands => add symbols commands
            foreach (var cmd in SupportedCommands)
            {
                var requiredSwitches = cmd.Options.Where(opt => opt.IsRequired).Select(opt => opt.LongName).ToList();
                if (!requiredSwitches.Any())
                {
                    continue;
                }
                
                // for each required switch, create a test case where that switch is omitted
                foreach (var selectedSwitch in requiredSwitches)
                {
                    var switchesToInclude = string.Join(" ", requiredSwitches
                        .Except(new[] { selectedSwitch })
                        .Select(switchName => $"--{switchName}"));
                    var cmdline = $"{cmd.Name} {switchesToInclude}";
                    yield return new[] { cmdline };
                }
            }
        }

        /// <summary>
        ///     For each public property of <see cref="DropConfig"/> and <see cref="DaemonConfig"/>
        ///     we add a command-line parameter to a command line, parse the command line, and assert that
        ///     the property of the resulting config object matches the value we passed on the command line.
        /// </summary>
        [Theory]
        [MemberData(nameof(ConfigOptionsTestData), typeof(DaemonConfig))]
        [MemberData(nameof(ConfigOptionsTestData), typeof(DropConfig))]
        public void TestDaemonConfigOptions(string cmdline, string jsonContent, PropertyInfo property, object expectedValue)
        {
            // construct and parse command line
            var fullCmdLine = ConstructCmdLine(cmdline, jsonContent);
            var cliConfig = ParseArgs(fullCmdLine);

            // get either DaemonConfig or DropConfig object from the parsed command line, based on given 'property'
            object configObject =
                property.DeclaringType == typeof(DaemonConfig) ? (object)ServicePipDaemon.CreateDaemonConfig(cliConfig) :
                property.DeclaringType == typeof(DropConfig) ? (object)global::Tool.DropDaemon.DropDaemon.CreateDropConfig(cliConfig) :
                null;

            // assert the value of that property is what we expect.
            Assert.NotNull(configObject);
            Assert.Equal(expectedValue, property.GetValue(configObject));
        }

        [Fact]
        public void TestConnectionErrorExitCode()
        {
            ConfiguredCommand conf = ParseArgs($"{StopDaemonCmd.Name} --{Moniker.LongName} 1 --{ConnectRetryDelayMillis.LongName} 10 --{MaxConnectRetries.LongName} 0");
            using (var client = CreateClient(conf))
            {
                var exitCode = conf.Command.ClientAction(conf, client);
                Assert.Equal((int)IpcResultStatus.ConnectionError, exitCode);
                client.RequestStop();
                client.Completion.GetAwaiter().GetResult();
            }
        }

        private string ConstructCmdLine(string cmdlinePrefix, string jsonContent = null, string cmdlineSuffix = null)
        {
            var json = jsonContent != null
                ? "-c " + QuoteStr(WriteToTmpFile(jsonContent))
                : string.Empty;
            return $"{cmdlinePrefix ?? string.Empty} {json} {cmdlineSuffix ?? string.Empty}";
        }

        public static IEnumerable<object[]> ConfigOptionsTestData(Type confType)
        {
            var cmd = StartCmd;
            var cmdline = cmd.Name + " --name 123 --service http://xyz "; // prepend required flags in all cases

            foreach (var prop in confType.GetProperties().Where(p => !p.Name.StartsWith("Default") && !p.Name.StartsWith("MaxConcurrentClients") && !p.Name.StartsWith("Logger")))
            {
                // For current 'prop', try to find a corresponding option ('opt') in 'cmd.Options' and pick a
                // representative value (according to its type) include in the command line.
                Option opt;
                var testValues = FindCorrespondingOptionAndRepresentativeValue(cmd.Options, prop, out opt);

                // Assert that we were able to match a corresponding option and generate a representative value for its type.
                var propStr = $"property '{confType.Name}.{prop.Name}'";
                Assert.True(opt != null, $"could not find option corresponding to {propStr}");
                Assert.True(testValues != null, $"Don't know how to generate a config value of type {prop.PropertyType.FullName} for {propStr}");

                // for each test value return 2 test cases: (1) when that value is set in the JSON config file, and (2) the value is set directly in the command line.
                foreach (var testValue in testValues)
                {
                    string cmdlineSwitchValue = testValue.Item1;
                    object configOptionValue = testValue.Item2;
                    var json = ToJsonStr(opt.LongName, cmdlineSwitchValue);
                    yield return new[] { cmdline, json, prop, configOptionValue };
                    yield return new[] { cmdline + $" --{opt.LongName} {cmdlineSwitchValue}", null, prop, configOptionValue };
                }
            }
        }

        private static string ToJsonStr(string key, string value)
        {
            return "{\"" + key + "\": " + value + "}";
        }

        private static Tuple<string, object> TupleCreate<T>(string item1, T item2) => Tuple.Create(item1, (object)item2);

        private static Tuple<string, object>[] FindCorrespondingOptionAndRepresentativeValue(IReadOnlyCollection<Option> options, PropertyInfo prop, out Option optionFound)
        {
            optionFound = options.FirstOrDefault(o =>
            {
                var suffixes = new[] { string.Empty, "Millis", "Days" };
                var optionName = o.LongName.Replace("tcp", string.Empty).Replace("Tcp", string.Empty);
                return suffixes.Any(s => string.Equals(optionName, prop.Name + s, StringComparison.OrdinalIgnoreCase));
            });

            if (optionFound == null)
            {
                return null;
            }

            var propType = prop.PropertyType;

            if (propType == typeof(int))
            {
                return new[]
                {
                    TupleCreate("1", 1),
                    TupleCreate("0", 0),
                    TupleCreate("10", 10)
                };
            }

            if (propType == typeof(bool))
            {
                return new[]
                {
                    TupleCreate("true", true),
                    TupleCreate("false", false)
                };
            }

            if (propType == typeof(string))
            {
                return new[]
                {
                    TupleCreate(QuoteStr("dummy"), "dummy")
                };
            }

            if (propType == typeof(IPAddress))
            {
                var val = "127.0.0.1";
                return new[]
                {
                    TupleCreate(QuoteStr(val), IPAddress.Parse(val))
                };
            }

            if (propType == typeof(Uri))
            {
                var val = "http://xyz";
                return new[]
                {
                    TupleCreate(QuoteStr(val), new Uri(val))
                };
            }

            if (propType == typeof(TimeSpan))
            {
                var val = 1000;
                var configOptionValue =
                    optionFound.LongName.EndsWith("Millis") ? TimeSpan.FromMilliseconds(val) :
                    optionFound.LongName.EndsWith("Days") ? TimeSpan.FromDays(val) :
                    TimeSpan.FromTicks(val);
                return new[]
                {
                    TupleCreate(val.ToString(), configOptionValue)
                };
            }

            return null;
        }

        private static string QuoteStr(object value)
        {
            return '"' + value.ToString() + '"';
        }

        private string WriteToTmpFile(string jsonContent)
        {
            var configFileName = System.IO.Path.Combine(TestOutputDirectory, "conf.json");
            System.IO.File.WriteAllText(configFileName, jsonContent);
            return configFileName;
        }

        private ConfiguredCommand ParseArgs(string fullCmdLine, bool ignoreInvalidOptions = false)
        {
            var logger = new LambdaLogger((level, format, args) => Output.WriteLine(format, args));
            return global::Tool.ServicePipDaemon.ServicePipDaemon.ParseArgs(fullCmdLine, UnixParser.Instance, logger, ignoreInvalidOptions: ignoreInvalidOptions);
        }
    }
}
