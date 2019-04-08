// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.DScript;
using TypeScript.Net.UnitTests.Utils;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using static TypeScript.Net.UnitTests.Utils.DiagnosticProcessor;

namespace TypeScript.Net.UnitTests.TypeChecking
{
    /// <summary>
    /// Set of file-based tests migrated from the TypeScript codebase
    /// </summary>
    public sealed class FileBasedCheckerTests
    {
        private const string FailingCasesDirectoryName = "FailingCases";
        private const string PassingCasesDirectoryName = "Cases";

        private readonly ITestOutputHelper m_output;

        public struct TestCaseResult
        {
            public string TestCase { get; set; }

            public bool Success { get; set; }

            public TimeSpan Duration { get; set; }

            public Exception Failure { get; set; }
        }

        public FileBasedCheckerTests(ITestOutputHelper output)
        {
            m_output = output;
        }

        // Uncomment following test case for debugging purposes
        // [Theory]
        // [InlineData("Cases/switchStatementsWithMultipleDefaults.ts")]
        // [InlineData("Cases/cf.ts")]
        // [InlineData("Cases/additionOperatorWithInvalidOperands.ts")]
        // [InlineData("Cases/circularModuleImports.ts")]
        // [InlineData("Cases/arrayConstructors1.ts")]
        
        public void DebugTypeCheckerFailure(string tsFileName)
        {
            CheckTypeScriptTestCases(tsFileName);
        }

        /// <summary>
        /// Runs all cases in parallel in one test.
        /// </summary>
        /// <remarks>
        /// Unfortunately, Xunit doesn't run parametrized tests in parallel that slows the overall process pretty significantly.
        /// So we're running all cases in one test, and the test will emit an error message that will help to diagnose all issues.
        /// </remarks>
        [Fact]
        public void RunAllTestCases()
        {
            var sw = Stopwatch.StartNew();

            // Running all the tests in parallel and capture duration for each of them.
            // This information is never used in a test, but could be helpful for diagnostic purposes.
            var files = GetAllTestFiles();
            var allFiles = files
                .AsParallel().WithDegreeOfParallelism(Environment.ProcessorCount)
                .Select((f, n) => ValidateFile(f))
                .ToList();

            var success = allFiles.Count(c => c.Success);

            if (success != allFiles.Count)
            {
                // We could have two types of errors: expected failure, like assertion violation or crash
                // We want to separate them to simplify debugging.
                var assertions = allFiles.Where(r => r.Failure is XunitException).ToList();
                int errors = assertions.Count;
                int crashes = allFiles.Count - success - errors;
                
                DumpFailedAssertions(assertions);
            }
        }

        private void DumpFailedAssertions(List<TestCaseResult> assertions)
        {
            string content = assertions.Aggregate(
                new StringBuilder(value: "Failed assertions:\r\n"),
                (sb, tc) =>
                {
                    sb.AppendLine($"Test case: {tc.TestCase}")
                        .AppendLine($"Duration: {tc.Duration}")
                        .AppendLine($"Failure: {tc.Failure}\r\n");
                    return sb;
                }).ToString();

            CustomAssert.Fail(content);
        }

        /// <summary>
        /// Runs the cases expected to fail in parallel in one test, and fails if any of them succeed.
        /// </summary>
        /// <remarks>
        /// There are still many tests that crash or fail. As we continue to fix bugs that enable more tests to pass,
        /// this run will guarantee that the passing tests get moved from "FailingCases" to "Cases" folder (see readme.md).
        /// </remarks>
        [Fact]
        public void RunFailingTestCases()
        {
            var sw = Stopwatch.StartNew();

            // Running all the tests in parallel and capture duration for each of them.
            // This information is never used in a test, but could be helpful for diagnostic purposes.
            var files = GetFailingTestFiles();
            var failingTestFiles = files
                .AsParallel().WithDegreeOfParallelism(Environment.ProcessorCount)
                .Select((f, n) => ValidateFile(f))
                .ToList();

            if (failingTestFiles.Any(c => c.Success))
            {
                var successes = failingTestFiles.Where(c => c.Success);

                StringBuilder message = new StringBuilder();
                message.AppendLine($"{successes.Count()} test cases passed (good job). Move them from {FailingCasesDirectoryName} folder to {PassingCasesDirectoryName} folder.");
                foreach (var success in successes)
                {
                    message.AppendLine($"move {success.TestCase.Replace(".ts", "*")} to {PassingCasesDirectoryName}\\");
                }

                CustomAssert.Fail(message.ToString());
            }

            // We make sure that none of the failing cases crashed (there is a special 'crashing' category
            // for these)
            if (failingTestFiles.Any(c => !(c.Failure is Xunit.Sdk.XunitException)))
            {
                var crashes = failingTestFiles.Where(c => !(c.Failure is Xunit.Sdk.XunitException));

                StringBuilder message = new StringBuilder();
                message.AppendLine($"{crashes.Count()} test(s) crashed. Move them from {FailingCasesDirectoryName} folder to CrashingCases folder.");
                foreach (var success in crashes)
                {
                    message.AppendLine($"move {success.TestCase.Replace(".ts", "*")} to CrashingCases\\");
                }

                CustomAssert.Fail(message.ToString());
            }
        }

        private TestCaseResult ValidateFile(string tsFileName)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                CheckTypeScriptTestCases(tsFileName);
                return new TestCaseResult
                {
                    TestCase = tsFileName,
                    Success = true,
                    Duration = sw.Elapsed
                };
            }
            catch (Exception e)
            {
                return new TestCaseResult()
                {
                    TestCase = tsFileName,
                    Failure = e,
                    Duration = sw.Elapsed,
                };
            }
        }

        /// <summary>
        /// Validates a given <paramref name="tsFileName"/>.
        /// </summary>
        private static void CheckTypeScriptTestCases(string tsFileName)
        {
            tsFileName = TypeScript.Net.Core.Path.NormalizePath(tsFileName);
            var tsFileContent = File.ReadAllText(tsFileName);
            var tsInputFile = new TestFile(tsFileName, tsFileContent);
            List<Diagnostic> diagnostics;
            
            // Getting diagnostic for the file.
            diagnostics = TypeCheckingHelper.ParseAndCheck(tsInputFile, ParsingOptions.TypeScriptParsingOptions);

            // Check that number of errors is correct
            var expectedErrorCount = GetExpectedErrorCount(tsFileName);

            if (expectedErrorCount == 0 && diagnostics.Count != 0)
            {
                // Special case: expecting no errors but got some
                string message =
                    $"Expecting 0 errors but got {diagnostics.Count}. All diagnostics: {DiagnosticMessages(diagnostics)}";

                CustomAssert.Fail(message);
            }

            if (expectedErrorCount != 0)
            {
                // Now, trying to compare actual content with an expected one.

                // To simplify future diagnostic, we're going to do following trick:

                // First, we'll compare content scrumbling file names in the error file (this will simplify migration
                // of the existing cases from TypeScript code base).
                // Second, if they're different we'll dump both expected and actual content.

                // So we're expecting errors, let's check that they're the same.
                var expectedErrorContent = ReadExpectedNormalizedErrorContent(tsFileName);

                // Dumping the file content for further diagnostic
                var actualErrorContent = GetActualNormalizedErrorContent(tsInputFile, diagnostics);

                try
                {
                    Assert.Equal(expectedErrorContent, actualErrorContent);
                }
                catch (Exception e)
                {
                    CustomAssert.Fail(
                        "Expected and actual files are different.\r\n" +
                        "Expected: \r\n" +
                        expectedErrorContent + "\r\n" +
                        "Actual: \r\n" +
                        actualErrorContent + "\r\n" +
                        "Original error:\r\n" + e.ToString());
                }
            }
        }

        private static string DiagnosticMessages(IEnumerable<Diagnostic> diagnostics)
        {
            return string.Join(Environment.NewLine, diagnostics.Select(d => d.MessageText.ToString()));
        }

        private static string ReadExpectedNormalizedErrorContent(string tsFileName)
        {
            var expectedErrorsFileName = GetExpectedFileNameWithErrors(tsFileName);
            var expectedErrorContent = File.ReadAllText(expectedErrorsFileName);
            expectedErrorContent = NormalizeFileName(content: expectedErrorContent, fileName: tsFileName);
            return expectedErrorContent;
        }

        private static string GetActualNormalizedErrorContent(TestFile tsInputFile, List<Diagnostic> diagnostics)
        {
            var actualErrorContent = GetErrorBaseline(tsInputFile, diagnostics);
            actualErrorContent = NormalizeFileName(content: actualErrorContent, fileName: tsInputFile.UnitName);

            return actualErrorContent;
        }

        private static string NormalizeFileName(string content, string fileName)
        {
            var lines = content.Split(new string[] { "\r\n" }, StringSplitOptions.None);

            // *.errors.txt file has a file name with a path inside that will never match with a current one.
            // So we need to replace it to make them equal.
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = FixFileNameInContent(lines[i], fileName);
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FixFileNameInContent(string line, string fileName)
        {
            string fileNameWithNoPath = Path.GetFileName(fileName);
            return Regex.Replace(line, "(.+?)?(tests/.+?" + fileNameWithNoPath + ")(.+?$)?", delegate(Match match)
            {
                return match.Groups[1] + fileName + match.Groups[3];
            });
        }

        private static int GetExpectedErrorCount(string tsFileName)
        {
            var fileNameWithErrors = GetExpectedFileNameWithErrors(tsFileName);
            if (!File.Exists(fileNameWithErrors))
            {
                // It means there should not be any errors.
                return 0;
            }

            var expectedErrorContent = File.ReadAllText(fileNameWithErrors);
            var expectedErrorCount = GetExpectedNumberOfErrors(expectedErrorContent);
            return expectedErrorCount;
        }

        private static string GetExpectedFileNameWithErrors(string tsFileName)
        {
            return tsFileName.Replace(".ts", ".errors.txt");
        }

        private static void CreateFolderIfNeeded(string fileName)
        {
            string path = Path.GetDirectoryName(fileName);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private static string[] s_allTests;
        private static string[] s_failingTests;

        private static string[] GetAllTestFiles()
        {
            return GetTestFiles(ref s_allTests, PassingCasesDirectoryName);
        }

        private static string[] GetFailingTestFiles()
        {
            return GetTestFiles(ref s_failingTests, FailingCasesDirectoryName);
        }

        private static string[] GetTestFiles(ref string[] testFiles, string directory)
        {
            LazyInitializer.EnsureInitialized(ref testFiles, () => Directory.EnumerateFiles(directory, "*.ts").ToArray());

            return testFiles;
        }

        private static int GetExpectedNumberOfErrors(string errorContent)
        {
            // This is hack and dirty way to compare that migrated typechecker is correct!
            // This method just parses expected output that has a string with number of errors and returns that number.
            var regex = new Regex("\\((?<errors>\\d+) errors\\)", RegexOptions.Multiline);
            var match = regex.Match(errorContent);

            if (match.Success)
            {
                return int.Parse(match.Groups["errors"].ToString());
            }

            return 0;
        }
    }
}
