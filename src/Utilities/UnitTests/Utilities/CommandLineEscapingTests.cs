// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
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
