// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class CommandLineEscapingTests : XunitBuildXLTest
    {
        private const string Invalid = "MarkerForInvalidCases";

        private readonly CommandLineEscapingTestCase[] m_cases =
        {
            // Some things don't need any escaping
            Case("foo"),
            Case(@"C:\foo"),
            Case(@"C:\foo\"),
            Case(@"\\unc\path"),
            Case(@"trail\\\"),
            Case(string.Empty, asWord: "\"\"", asApplicationName: Invalid),

            // Spaces need surrounding double quotes
            Case("foo bar", asWord: "\"foo bar\"", asApplicationName: "\"foo bar\""),
            Case("foo\tbar", asWord: "\"foo\tbar\"", asApplicationName: "\"foo\tbar\""),

            // Application names don't need leading / trailing whitespace (since that's not valid in a path).
            // We choose to be consistent with Process.BuildCommandLine (private; see reference sources) and strip whitespace.
            Case("   f   ", asWord: "\"   f   \"", asApplicationName: "f"),

            // Double quotes need to be escaped for words, but don't make sense in an application name
            Case(@"foo""bar", asWord: @"foo\""bar", asApplicationName: Invalid),
            Case(@"My name is ""bar""", asWord: @"""My name is \""bar\""""", asApplicationName: Invalid),

            // A path name with spaces might have a trailing slash. Words use slashes as an escape, but application names do not.
            // Note that words only respect \ when it precedes a quote, hence the fixpoint examples at the top.
            Case(@"C:\Program Files\", asWord: @"""C:\Program Files\\""", asApplicationName: @"""C:\Program Files\"""),
            Case(@"C:\Program Files\", asWord: @"""C:\Program Files\\""", asApplicationName: @"""C:\Program Files\"""),

            // Double escaping. For clarity, the un-C# versions:
            //   "C:\Program Files\\" -> "\"C:\Program Files\\\\\""
            Case(@"""C:\Program Files\\""", asWord: @"""\""C:\Program Files\\\\\""""", asApplicationName: Invalid),

            // Double escaping with interior quotes:
            //   x\\\"y\\\"x -> x\\\\\\\"y\\\\\\\"x
            Case(@"x\\\""y\\\""x", asWord: @"x\\\\\\\""y\\\\\\\""x", asApplicationName: Invalid),
        };


        public CommandLineEscapingTests(ITestOutputHelper output)
            : base(output) { }

        [Theory]
        [InlineData("DEBUG;TRACE;DEFTEMP", "\"DEBUG;TRACE;DEFTEMP\"", "DEBUG;TRACE;DEFTEMP")]
        [InlineData("(A)", "\"(A)\"", "(A)")]
        [InlineData("`A`", "\"`A`\"", "`A`")]
        public void TestPlatformSpecificWordEscaping(string value, string asWordOnUnix, string asWordOnWindows)
        {
            var expected = OperatingSystemHelper.IsUnixOS
                ? asWordOnUnix
                : asWordOnWindows;
            XAssertEscapingCase(expected, value, CommandLineEscaping.EscapeAsCommandLineWord);
        }

        [Fact]
        public void TestCommandLineWordEscaping()
        {
            foreach (CommandLineEscapingTestCase escapingCase in m_cases)
            {
                XAssertEscapingCase(escapingCase.AsWord, escapingCase.Value, CommandLineEscaping.EscapeAsCommandLineWord);
            }
        }

        [Fact]
        public void TestApplicationNameEscaping()
        {
            foreach (CommandLineEscapingTestCase escapingCase in m_cases)
            {
                XAssertEscapingCase(escapingCase.AsApplicationName, escapingCase.Value, CommandLineEscaping.EscapeAsCreateProcessApplicationName);
            }
        }

#if NET_CORE

        [Fact]
        public void ParseEmptyStringReturnsZeroLengthList()
        {
            List<CommandLineEscaping.Arg> args = CommandLineEscaping.SplitArguments(string.Empty).ToList();
            XAssert.AreEqual(0, args.Count);
        }

        [Theory]
        [InlineData("\"\"")]
        [InlineData(" \"\"")]
        [InlineData("\"\" ")]
        public void ParseEmptyArg(string param)
        {
            List<CommandLineEscaping.Arg> args = CommandLineEscaping.SplitArguments(param).ToList();
            string msg = $"param: {param}";
            XAssert.AreEqual(1, args.Count, msg);
            XAssert.AreEqual(string.Empty, args[0].Value.ToString(), msg);
        }

        [Fact]
        public void ParseTrailingEmptyArg()
        {
            const string param = "Arg1 \"\"";
            List<CommandLineEscaping.Arg> args = CommandLineEscaping.SplitArguments(param).ToList();
            string msg = $"param: {param}";
            XAssert.AreEqual(2, args.Count, msg);
            XAssert.AreEqual("Arg1", args[0].Value.ToString(), msg);
            XAssert.AreEqual(string.Empty, args[1].Value.ToString(), msg);
            XAssert.AreEqual("\"\"", args[1].Raw.ToString(), msg);
        }

        [Fact]
        public void ParseLeadingEmptyArg()
        {
            const string param = "\"\" Arg2";
            List<CommandLineEscaping.Arg> args = CommandLineEscaping.SplitArguments(param).ToList();
            string msg = $"param: {param}";
            XAssert.AreEqual(2, args.Count, msg);
            XAssert.AreEqual(string.Empty, args[0].Value.ToString(), msg);
            XAssert.AreEqual("\"\"", args[0].Raw.ToString(), msg);
            XAssert.AreEqual("Arg2", args[1].Value.ToString(), msg);
        }

        [Theory]
        [InlineData("param")]
        [InlineData("param   ")]
        [InlineData("    param   ")]
        [InlineData("    param")]
        [InlineData("\"param\"")]
        [InlineData("\"param")]
        [InlineData("pa\"r\"am")]
        [InlineData("p\"a\"r\"a\"m")]
        public void ParseQuotedParam(string param)
        {
            List<CommandLineEscaping.Arg> args = CommandLineEscaping.SplitArguments(param).ToList();
            string msg = $"param: {param}";
            XAssert.AreEqual(1, args.Count, msg);
            XAssert.AreEqual("param", args[0].Value.ToString(), msg);
            XAssert.AreEqual(param.Trim(), args[0].Raw.ToString(), msg);
        }

        [Theory]
        [InlineData("\"one\\\"two\"", "one\"two")]
        [InlineData("one\\\"two", "one\"two")]
        public void ParseEscapedQuotes(string param, string expectedArg)
        {
            List<CommandLineEscaping.Arg> args = CommandLineEscaping.SplitArguments(param).ToList();
            XAssert.AreEqual(1, args.Count);
            XAssert.AreEqual(expectedArg, args[0].Value.ToString());
            XAssert.AreEqual(param, args[0].Raw.ToString());
        }

        [Theory]
        // from https://docs.microsoft.com/en-us/cpp/cpp/main-function-command-line-args?redirectedfrom=MSDN&view=msvc-170#results-of-parsing-command-lines
        [InlineData(@"""abc"" d e",       "abc", "d", "e")]
        [InlineData(@"a\\\""b c d""",    @"a\""b", "c", "d")]
        [InlineData(@"a\\b d""e f""g h", @"a\\b", "de fg", "h")]
        [InlineData(@"a\\\\""b c"" d e", @"a\\b c", "d", "e")]
        // from https://docs.microsoft.com/en-us/cpp/c-language/parsing-c-command-line-arguments?redirectedfrom=MSDN&view=msvc-170
        [InlineData(@"""a b c"" d e",       "a b c", "d", "e")]
        [InlineData(@"""ab\""c"" ""\\"" d", "ab\"c", "\\", "d")]
        public void ParseMsvcExamples(string param, string arg1, string arg2, string arg3)
        {
            List<string> args = CommandLineEscaping.SplitArguments(param, useWindowsRules: true).Select(t => t.Value.ToString()).ToList().ToList();
            string msg = $"\nparams: {param}\nargs: " + string.Join("\n- ", args);
            XAssert.AreEqual(arg1, args.FirstOrDefault(), msg);
            XAssert.AreEqual(arg2, args.Skip(1).FirstOrDefault(), msg);
            XAssert.AreEqual(arg3, args.Skip(2).FirstOrDefault(), msg);
        }

        // on Windows only: double quote while quoting escapes the second quote
        [Theory]
        [InlineData(true, @"a""b"""" c d", @"ab"" c d")]
        [InlineData(false, @"a""b"""" c d", @"ab c d")]
        [InlineData(true, "\"\"\"\"", "\"")]
        [InlineData(false, "\"\"\"\"", "")]
        public void ParseDoubleQuote(bool isWindows, string param, string expectedArg)
        {
            List<string> args = CommandLineEscaping.SplitArguments(param, isWindows).Select(m => m.Value.ToString()).ToList().ToList();
            string msg = $"\nparams: {param}\nargs: " + string.Join("\n- ", args);
            XAssert.AreEqual(expectedArg, args.FirstOrDefault(), msg);
            XAssert.AreEqual(1, args.Count, msg);
        }

        [Fact]
        public void ParseEscapedStuff()
        {
            const string param = "\"-DCMAKE_CFG_INTDIR=\\\".\\\"\" \"-D_DEBUG\" \"-D_GNU_SOURCE\"";
            List<string> args = CommandLineEscaping.SplitArguments(param).Select(t => t.Value.ToString()).ToList().ToList();
            string msg = $"\nparams: {param}\nargs: " + string.Join("|", args);
            XAssert.AreEqual(3, args.Count, msg);
            XAssert.AreEqual("-DCMAKE_CFG_INTDIR=\".\"", args[0], msg);
            XAssert.AreEqual("-D_DEBUG", args[1], msg);
            XAssert.AreEqual("-D_GNU_SOURCE", args[2], msg);
        }

        [Fact]
        public void ParseEscapedSpaceOnLinux()
        {
            const string param = "one\\ two";
            List<string> args = CommandLineEscaping.SplitArguments(param, useWindowsRules: false).Select(t => t.Value.ToString()).ToList().ToList();
            string msg = $"\nparams: {param}\nargs: " + string.Join("|", args);
            XAssert.AreEqual(1, args.Count, msg);
            XAssert.AreEqual("one two", args[0], msg);
        }

        [Fact]
        public void ParseQuotedParam()
        {
            const string param = "\"param\"";
            List<CommandLineEscaping.Arg> args = CommandLineEscaping.SplitArguments(param).ToList();
            XAssert.AreEqual(1, args.Count);
            XAssert.AreEqual("param", args[0].Value.ToString());
            XAssert.AreEqual(param, args[0].Raw.ToString());
        }

        [Fact]
        public void ParseQuotedParamNoEndQuote()
        {
            const string param = "\"param";
            List<CommandLineEscaping.Arg> args = CommandLineEscaping.SplitArguments(param).ToList();
            XAssert.AreEqual(1, args.Count);
            XAssert.AreEqual("param", args[0].Value.ToString());
            XAssert.AreEqual(param, args[0].Raw.ToString());
        }

        [Fact]
        public void ParseQuotedParamWithSpaces()
        {
            const string param = "\"param  1\"";
            List<CommandLineEscaping.Arg> args = CommandLineEscaping.SplitArguments(param).ToList();
            XAssert.AreEqual(1, args.Count);
            XAssert.AreEqual("param  1", args[0].Value.ToString());
            XAssert.AreEqual(param, args[0].Raw.ToString());
        }

        [Fact]
        public void ParseParamsWithLotsOfWhitespace()
        {
            List<CommandLineEscaping.Arg> args = CommandLineEscaping.SplitArguments("param     1 2\t\t\t\t3").ToList();
            XAssert.AreEqual(4, args.Count);
            XAssert.AreEqual("param", args[0].Value.ToString());
            XAssert.AreEqual("param", args[0].Raw.ToString());

            XAssert.AreEqual("1", args[1].Value.ToString());
            XAssert.AreEqual("1", args[1].Raw.ToString());

            XAssert.AreEqual("2", args[2].Value.ToString());
            XAssert.AreEqual("2", args[2].Raw.ToString());

            XAssert.AreEqual("3", args[3].Value.ToString());
            XAssert.AreEqual("3", args[3].Raw.ToString());
        }

        [Fact]
        public void ParsingWithAndWithoutQuotes()
        {
            List<CommandLineEscaping.Arg> args =
                CommandLineEscaping.SplitArguments("/b:abc -foo \"ba\"r   /bar:\"blah\" \"/q:quoted \"  /tail:\"Unclosed Quote").ToList();
            XAssert.AreEqual(6, args.Count);
            XAssert.AreEqual("/b:abc", args[0].Value.ToString());
            XAssert.AreEqual("/b:abc", args[0].Raw.ToString());

            XAssert.AreEqual("-foo", args[1].Value.ToString());
            XAssert.AreEqual("-foo", args[1].Raw.ToString());

            XAssert.AreEqual("bar", args[2].Value.ToString());
            XAssert.AreEqual("\"ba\"r", args[2].Raw.ToString());

            XAssert.AreEqual("/bar:blah", args[3].Value.ToString());
            XAssert.AreEqual("/bar:\"blah\"", args[3].Raw.ToString());

            XAssert.AreEqual("/q:quoted ", args[4].Value.ToString());
            XAssert.AreEqual("\"/q:quoted \"", args[4].Raw.ToString());

            XAssert.AreEqual("/tail:Unclosed Quote", args[5].Value.ToString());
            XAssert.AreEqual("/tail:\"Unclosed Quote", args[5].Raw.ToString());
        }
#endif

        private static CommandLineEscapingTestCase Case(string value, string asWord = null, string asApplicationName = null)
        {
            return new CommandLineEscapingTestCase
                   {
                       Value = value,
                       AsWord = asWord ?? value,
                       AsApplicationName = asApplicationName ?? value
                   };
        }

        private static void XAssertEscapingCase(string expected, string value, Func<string, string> escaper)
        {
            if (!ReferenceEquals(expected, Invalid))
            {
                XAssert.AreEqual(expected, escaper(value));
            }
        }
    }

    internal struct CommandLineEscapingTestCase
    {
        public string Value;
        public string AsWord;
        public string AsApplicationName;
    }
}
