// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Text.RegularExpressions;
using BuildXL.FrontEnd.Script.Tracing;
using Test.DScript.Ast.DScriptV2;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.ErrorHandling
{
    public sealed class TestStackTrace : DScriptV2Test
    {
        private static Regex s_fileLocationRegex = new Regex(@"\w+\(\D+,\D+\)");
        private const string Spec1FileName = "spec1.dsc";
        private const string Spec2FileName = "spec2.dsc";
        private const string Spec3FileName = "spec3.dsc";
        private const string Spec4FileName = "spec4.dsc";
        private const string RuntimeFailureExpression = "[][2]";
        private const int ExpectedEventId = (int)LogEventId.ArrayIndexOufOfRange;

        public TestStackTrace(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void TestSimpleStackTrace()
        {
            string spec = $"export const x = {RuntimeFailureExpression};";

            var result =
                BuildWithPrelude()
                    .AddFile("package.config.dsc", CreatePackageConfig("MyPackage", true, Spec1FileName))
                    .AddSpec(Spec1FileName, spec)
                    .RootSpec(Spec1FileName)
                    .EvaluateWithFirstError("x");

            Assert.NotNull(result.Location);
            Assert.Equal(ExpectedEventId, result.ErrorCode);
            ValidateStackTrace(
                result.FullMessage, 
                $"{Spec1FileName}(1,{spec.IndexOf(RuntimeFailureExpression, StringComparison.OrdinalIgnoreCase) + 1})");
        }

        [Fact]
        public void TestTwoLevelStackTrace()
        {
            string spec2 = $"@@public export function y() {{ return {RuntimeFailureExpression}; }};";
            string spec1 = "export const x = y();";

            var result =
                BuildWithPrelude()
                    .AddFile("package.config.dsc", CreatePackageConfig("MyPackage", true, Spec1FileName, Spec2FileName))
                    .AddFile(Spec2FileName, spec2)
                    .AddFile(Spec1FileName, spec1)
                    .RootSpec(Spec1FileName)
                    .EvaluateWithFirstError("x");

            Assert.NotNull(result.Location);
            Assert.Equal(ExpectedEventId, result.ErrorCode);
            ValidateStackTrace(
                result.FullMessage, 
                $"{Spec2FileName}(1,{GetIndex(spec2, RuntimeFailureExpression)}): at y", 
                $"{Spec1FileName}(1,{GetIndex(spec1, "y()")})");
        }

        [Fact]
        public void TestFourLevelStackTrace()
        {
            string spec4 = $"@@public export function z() {{ return {RuntimeFailureExpression}; }}";
            string spec3 = "@@public export function y() { return z(); }";
            string spec2 = "@@public export function x() { return y(); }";
            string spec1 = "export const w = x();";

            var result =
                BuildWithPrelude()
                    .AddFile("package.config.dsc", CreatePackageConfig("MyPackage", true, Spec1FileName, Spec2FileName, Spec3FileName, Spec4FileName))
                    .AddFile(Spec4FileName, spec4)
                    .AddFile(Spec3FileName, spec3)
                    .AddFile(Spec2FileName, spec2)
                    .AddFile(Spec1FileName, spec1)
                    .RootSpec(Spec1FileName)
                    .EvaluateWithFirstError("w");

            Assert.NotNull(result.Location);
            Assert.Equal(ExpectedEventId, result.ErrorCode);
            ValidateStackTrace(
                result.FullMessage, 
                $"{Spec4FileName}(1,{GetIndex(spec4, RuntimeFailureExpression)}): at z", 
                $"{Spec3FileName}(1,{GetIndex(spec3, "z()")}): at y", 
                $"{Spec2FileName}(1,{GetIndex(spec2, "y()")}): at x", 
                $"{Spec1FileName}(1,{GetIndex(spec1, "x()")})");
        }

        [Fact]
        public void TestFourLevelStackTraceUnwind()
        {
            string spec4 = $"@@public export function z() {{ return {RuntimeFailureExpression}; }}";
            string spec3 = "@@public export function y() { return 42; }";
            string spec2 = "@@public export function x() { let foo = y(); return z(); }";
            string spec1 = "export const w = x();";

            var result =
                BuildWithPrelude()
                    .AddFile("package.config.dsc", CreatePackageConfig("MyPackage", true, Spec1FileName, Spec2FileName, Spec3FileName, Spec4FileName))
                    .AddFile(Spec4FileName, spec4)
                    .AddFile(Spec3FileName, spec3)
                    .AddFile(Spec2FileName, spec2)
                    .AddFile(Spec1FileName, spec1)
                    .RootSpec(Spec1FileName)
                    .EvaluateWithFirstError("w");

            Assert.NotNull(result.Location);
            Assert.Equal(ExpectedEventId, result.ErrorCode);
            ValidateStackTrace(
                result.FullMessage,
                $"{Spec4FileName}(1,{GetIndex(spec4, RuntimeFailureExpression)}): at z",
                $"{Spec2FileName}(1,{GetIndex(spec2, "z()")}): at x", 
                $"{Spec1FileName}(1,{GetIndex(spec1, "x()")})");
        }

        private void ValidateStackTrace(string fullMessage, params string[] locations)
        {
            Assert.NotNull(fullMessage);
            var stackTraceIndex = fullMessage.IndexOf("Stack trace:");
            Assert.True(stackTraceIndex > 0);
            var stackTrace = fullMessage.Substring(stackTraceIndex);

            foreach (string location in locations)
            {
                // Find the file location string in the stack trace
                var index = stackTrace.IndexOf(location);

                // Validate the location string was found
                Assert.True(index > 0, $"Expected: {location}\r\n\r\nCurrent: {stackTrace}\r\n\r\nOriginal: {fullMessage}");

                // Validate that another location doesn't exist before the location which was found
                Assert.DoesNotMatch(s_fileLocationRegex, stackTrace.Substring(0, index));

                // Reduce the stack trace to prepare for the next location
                stackTrace = stackTrace.Substring(index + location.Length);
            }
        }

        private int GetIndex(string spec, string token)
        {
            return spec.IndexOf(token, StringComparison.OrdinalIgnoreCase) + 1;
        }
    }
}
