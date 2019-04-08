// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Analyzer.Analyzers;
using BuildXL.FrontEnd.Script.Analyzer.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using Xunit;

namespace Test.Tool.DScript.Analyzer
{
    public sealed class PrettyPrintDiffTests
    {
        [Fact]
        public void DiffErrorSingleLineStartChar()
        {
            DiffTest("abc def ghi", "Abc def ghi",
@"b:\f\a.txt(1,1): Non-standard formatting encountered. Encountered: 'A' expected: 'a' in line:
Abc def ghi
^");
        }

        [Fact]
        public void DiffErrorSingleMiddleChar()
        {
            DiffTest("abc def ghi", "abc dEf ghi",
@"b:\f\a.txt(1,6): Non-standard formatting encountered. Encountered: 'E' expected: 'e' in line:
abc dEf ghi
     ^");
        }

        [Fact]
        public void DiffErrorSingleLineLastChar()
        {
            DiffTest("abc def ghi", "abc def ghI",
@"b:\f\a.txt(1,11): Non-standard formatting encountered. Encountered: 'I' expected: 'i' in line:
abc def ghI
          ^");
        }

        [Fact]
        public void DiffErrorSingleLineExtraChars()
        {
            DiffTest("abc def", "abc def ghi",
@"b:\f\a.txt(1,7): Non-standard formatting encountered. Encountered: ' ghi' expected: '<newline>' in line:
abc def ghi
       ^");
        }

        [Fact]
        public void DiffErrorSingleLineMissingChars()
        {
            DiffTest("abc def ghi", "abc def",
@"b:\f\a.txt(1,7): Non-standard formatting encountered. Encountered: '<newline>' expected: ' ghi' in line:
abc def ghi
       ^");
        }

        [Fact]
        public void DiffErrorExtraLine()
        {
            DiffTest(
@"abc
def",

@"abc
def
ghi",

@"b:\f\a.txt(2,0): Non-standard formatting encountered. Encountered an extra line:
ghi");
        }

        [Fact]
        public void DiffErrorMissingLine()
        {
            DiffTest(
@"abc
def
ghi",

@"abc
def",

@"b:\f\a.txt(2,0): Non-standard formatting encountered. Encountered a missing line. Expected line:
ghi");
        }

        private void DiffTest(string expected, string encountered = null, string message = null)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                expected = expected.Replace("\r", "");
                encountered = encountered.Replace("\r", "");
                message = message.Replace("\r", "");
            }

            encountered = encountered ?? expected;

            var logger = Logger.CreateLogger(preserveLogEvents: true);
            var loggingContext = new LoggingContext("UnitTest");

            PrettyPrint.ReportFirstDifference(logger, loggingContext, encountered, expected, @"b:\f\a.txt");

            if (message == null)
            {
                Assert.False(logger.HasErrors, "Did not expect to encounter errors");
            }
            else
            {
                Assert.True(logger.HasErrors, "Expected to encounter an error");
                Assert.Equal(1, logger.ErrorCount);
                var diagnostic = logger.CapturedDiagnostics[0];
                var msg = OperatingSystemHelper.IsUnixOS ? diagnostic.Message.Replace("\r", "") : diagnostic.Message;
                Assert.Equal(message, msg);
            }
        }
    }
}
