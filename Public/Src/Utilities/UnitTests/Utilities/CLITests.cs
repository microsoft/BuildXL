// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BuildXL.Utilities.CLI;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public class CLITests
    {
        [Theory]
        [InlineData(@"arg1 arg2 arg3", new[] { "arg1", "arg2", "arg3" })]
        [InlineData(@"arg1 ""quoted arg2"" arg3", new[] { "arg1", "quoted arg2", "arg3" })]
        [InlineData(@"arg1 escaped\ arg2 arg3", new[] { "arg1", "escaped arg2", "arg3" })]
        [InlineData(@"arg1 escaped\""quote2 escaped\""quote3", new[] { "arg1", "escaped\"quote2", "escaped\"quote3" })]
        [InlineData("arg1 \"unclosed quote", new[] { "arg1", "unclosed quote" })]
        [InlineData(@"--""key with space"" value", new[] { "--key with space", "value" })]
        [InlineData(@"--""key with space"" value\ with"" ""space", new[] { "--key with space", "value with space" })]
        [InlineData(@"/key:""value with space""", new[] { "/key:value with space" })]
        [InlineData(@"/key:""value with""\ space", new[] { "/key:value with space" })]
        [InlineData(@"--key ""C:\x\y\z.txt""", new[] { "--key", @"C:\x\y\z.txt" })]
        [InlineData(@"/key:""C:\x\y.txt""", new[] { @"/key:C:\x\y.txt" })]
        [InlineData(@"/key:C:\\x\\y.txt", new[] { @"/key:C:\x\y.txt" })]
        public void SplitArgsTests(string input, string[] expectedArgs)
        {
            var actualArgs = AbstractParser.CommonSplitArgs(input);
            Assert.Equal(expectedArgs, actualArgs);
        }

        [Theory]
        [InlineData(typeof(WinParser), new[] { "/key:value" },             0, PrefixKind.Either, "key",             "value")]
        [InlineData(typeof(WinParser), new[] { "/key" },                   0, PrefixKind.Either, "key",             null)]
        [InlineData(typeof(WinParser), new[] { "/key+" },                  0, PrefixKind.Either, "key",             "true")]
        [InlineData(typeof(WinParser), new[] { "/key-" },                  0, PrefixKind.Either, "key",             "false")]
        [InlineData(typeof(WinParser), new[] { "/key:value with spaces" }, 0, PrefixKind.Either, "key",             "value with spaces")]
        [InlineData(typeof(WinParser), new[] { "/key with spaces:value" }, 0, PrefixKind.Either, "key with spaces", "value")]
        [InlineData(typeof(WinParser), new[] { "value" },                  0, PrefixKind.None,   "",                "value")]
        [InlineData(typeof(WinParser), new[] { "value:too" },              0, PrefixKind.None,   "",                "value:too")]
        [InlineData(typeof(WinParser), new[] { "value with spaces" },      0, PrefixKind.None,   "",                "value with spaces")]
        [InlineData(typeof(WinParser), new[] { @"/key:C:\x\y.txt" },       0, PrefixKind.Either, "key",             @"C:\x\y.txt")]

        [InlineData(typeof(UnixParser), new[] { "--key", "C:\\x\\y.txt" },      0, PrefixKind.Long, "key",              @"C:\x\y.txt")]
        [InlineData(typeof(UnixParser), new[] { "--key", "value" },             0, PrefixKind.Long, "key",              "value")]
        [InlineData(typeof(UnixParser), new[] { "--key" },                      0, PrefixKind.Long, "key",              null)]
        [InlineData(typeof(UnixParser), new[] { "-k" },                         0, PrefixKind.Short, "k",               null)]
        [InlineData(typeof(UnixParser), new[] { "-k", "value" },                0, PrefixKind.Short, "k",               "value")]
        [InlineData(typeof(UnixParser), new[] { "--key", "value with spaces" }, 0, PrefixKind.Long,  "key",             "value with spaces")]
        [InlineData(typeof(UnixParser), new[] { "--key with spaces", "value" }, 0, PrefixKind.Long,  "key with spaces", "value")]
        [InlineData(typeof(UnixParser), new[] { "-key with spaces", "value" },  0, PrefixKind.Short, "key with spaces", "value")]
        [InlineData(typeof(UnixParser), new[] { "value" },                      0, PrefixKind.None,  "",                "value")]
        [InlineData(typeof(UnixParser), new[] { "value with spaces" },          0, PrefixKind.None,  "",                "value with spaces")]
        [InlineData(typeof(UnixParser), new[] { "value", "next value" },        1, PrefixKind.None,  "",                "value")]
        [InlineData(typeof(UnixParser), new[] { "--key", "--next-key" },        1, PrefixKind.Long,  "key",             null)]
        [InlineData(typeof(UnixParser), new[] { "--key", "-n" },                1, PrefixKind.Long,  "key",             null)]
        [InlineData(typeof(UnixParser), new[] { "-k", "--next-key" },           1, PrefixKind.Short, "k",               null)]
        [InlineData(typeof(UnixParser), new[] { "-k", "-n" },                   1, PrefixKind.Short, "k",               null)]
        public void Parser_ParseTests(Type parserType, string[] args, int expectedRemainingArgs, PrefixKind expectedPrefix, string expectedKey, string expectedValue)
        {
            var parser = GetParserForType(parserType);
            var queue = new Queue<string>(args);
            var parsedOption = parser.Parse(queue);
            Assert.Equal(expectedRemainingArgs, queue.Count);
            AssertOption(expectedPrefix, expectedKey, expectedValue, parsedOption);

            // ensure that when render and then re-parse we get the same ParsedOption
            var rendered = parser.RenderSingleOption(expectedPrefix, expectedKey, expectedValue);
            var regenArgs = parser.SplitArgs(rendered);
            var reparsedOption = parser.Parse(new Queue<string>(regenArgs));
            AssertOption(expectedPrefix, expectedKey, expectedValue, reparsedOption);
        }

        [Theory(Skip = "[StressTest]")]
        [InlineData(typeof(WinParser), 1000000, 10)]
        [InlineData(typeof(UnixParser), 1000000, 10)]
        public void Parser_StressTestLargeInput(Type parserType, int size, int timeLimitSeconds)
        {
            var opt1 = new Option("opt1") { IsMultiValue = true };
            var parser = GetParserForType(parserType);

            var sw = Stopwatch.StartNew();
            string input = string.Join(" ", Enumerable
                .Range(0, size)
                .Select(i => parser.RenderSingleOption(new ParsedOption(PrefixKind.Long, opt1.LongName, $"long/option/value/{i}/xxxxxxxxxx/yyyyyyyy/zzzzzzzzz/zzzzzzz"))));
            var queue = new Queue<string>(parser.SplitArgs(input));
            var conf = Config.ParseCommandLineArgs(new[] { opt1 }, queue, parser);
            var totalTime = sw.Elapsed;

            XAssert.IsTrue(totalTime.TotalSeconds <= timeLimitSeconds, $"Parsing failed to complete within {timeLimitSeconds}s; instead, it took {totalTime}");

            var optValues = conf.ConfiguredOptionValues[opt1];
            XAssert.AreEqual(size, optValues.Count());
        }

        private void AssertOption(PrefixKind expectedPrefix, string expectedKey, string expectedValue, ParsedOption actualOption)
        {
            Assert.Equal(expectedPrefix, actualOption.PrefixKind);
            Assert.Equal(expectedKey, actualOption.Key);
            Assert.Equal(expectedValue, actualOption.Value);
        }

        private static IParser GetParserForType(Type parserType)
        {
            var parser =
                parserType == typeof(UnixParser) ? UnixParser.Instance :
                parserType == typeof(WinParser) ? WinParser.Instance :
                (IParser)null;
            XAssert.IsNotNull(parser, "Unknown parser type: " + parserType);
            return parser;
        }
    }
}
