// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Sdk.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Set of extension methods for <see cref="TestResult"/> class.
    /// </summary>
    public static class TestResultExtensions
    {
        private static global::BuildXL.FrontEnd.Core.Tracing.LogEventId[] s_checkerErrors = new[]
        {
            global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CheckerError,
            global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CheckerGlobalError,
            global::BuildXL.FrontEnd.Core.Tracing.LogEventId.TypeScriptSyntaxError,
            global::BuildXL.FrontEnd.Core.Tracing.LogEventId.TypeScriptLocalBindingError
        };
        private static HashSet<int> s_checkerErrorCodes = new HashSet<int>(s_checkerErrors.Select(e => (int)e));

        /// <summary>
        /// Converts <paramref name="result"/> instance to <see cref="EvaluatedValues"/>.
        /// </summary>
        public static EvaluatedValues NoErrors(this TestResult result, string expression)
        {
            return NoErrors(result, new[] {expression});
        }
        
        /// <summary>
        /// Converts <paramref name="result"/> instance to <see cref="EvaluatedValues"/>.
        /// </summary>
        public static EvaluatedValues NoErrors(this TestResultBase result)
        {
            if (result.HasError)
            {
                string failures = string.Join(Environment.NewLine, result.Diagnostics.Select(e => e.FullMessage));
                throw new InvalidOperationException(
                   I($"Evaluation failed. Errors: {failures}"));
            }

            return EvaluatedValues.Empty;
        }

        /// <summary>
        /// Converts <paramref name="result"/> instance to <see cref="EvaluatedValues"/>.
        /// </summary>
        public static EvaluatedValues NoErrors(this TestResult result, ICollection<string> evaluatedExpressions)
        {
            Contract.Requires(result != null);
            Contract.Requires(evaluatedExpressions != null);

            if (result.HasError)
            {
                string failures = string.Join(Environment.NewLine, result.Diagnostics.Select(e => e.FullMessage));
                throw new InvalidOperationException(
                   I($"Evaluation failed. Errors: {failures}"));
            }

            if (result.ValueCount != evaluatedExpressions.Count)
            {
                throw new InvalidOperationException(
                   I($"Expected '{evaluatedExpressions.Count}' expressions but got '{result.ValueCount}'"));
            }

            var expressionMap = evaluatedExpressions.Select((e, idx) => Tuple.Create(e, result.Values[idx])).ToList();
            return new EvaluatedValues(expressionMap);
        }

        /// <nodoc />
        public static void ExpectValues(this TestResult testResult, int count)
        {
            Contract.Requires(testResult != null);
            Contract.Requires(count >= 0);

            XAssert.AreEqual(
                count,
                testResult.ValueCount,
                "Errors: {0}",
                string.Join(Environment.NewLine, testResult.Diagnostics.Where(e => e.Level.IsError()).Select(x => x.FullMessage)));
        }

        /// <nodoc />
        public static void ExpectNoError(this TestResultBase testResult)
        {
            Contract.Requires(testResult != null);
            ExpectErrors(testResult, count: 0);
        }

        /// <nodoc />
        public static void ExpectErrors(this TestResultBase testResult, int count)
        {
            Contract.Requires(testResult != null);
            Contract.Requires(count >= 0);

            XAssert.AreEqual(
                count,
                testResult.ErrorCount,
                "Errors: {0}",
                string.Join(Environment.NewLine, testResult.Diagnostics.Where(e => e.Level.IsError()).Select(x => x.FullMessage)));
        }

        /// <nodoc />
        public static void ExpectErrorMessageSubstrings(this TestResultBase testResult, string[] messages, bool useFullMessage = false)
        {
            Contract.Requires(testResult != null);
            Contract.Requires(messages != null);
            Contract.RequiresForAll(messages, m => m != null);

            foreach (var message in messages)
            {
                bool found =
                    testResult.Diagnostics.Any(dsError => !useFullMessage ? dsError.Message.Contains(message) : dsError.FullMessage.Contains(message));
                var diagnostics = string.Join(Environment.NewLine, testResult.Diagnostics.Select(e => e.Message));

                Assert.True(found,
                   I($"Expect error messages to contain '{message}'. All errors: \r\n{diagnostics}"));
            }
        }

        /// <nodoc />
        public static Diagnostic[] ExpectCheckerDiagnostic(this TestResultBase result, TypeScript.Net.Diagnostics.IDiagnosticMessage expected, params string[] args)
        {
            var checkerDiagnostics = result.Errors.Where(e => s_checkerErrorCodes.Contains(e.ErrorCode)).ToArray();
            if (!checkerDiagnostics.Any())
            {
                var msg = string.Join(", ", s_checkerErrors.Select(e => $"'{e}'"));
                XAssert.Fail($"Didn't find any checher errors.  Expected one of: {msg}.  Actual: {result}");
            }

            var formatted = string.Format(expected.Message, args);
            if (!checkerDiagnostics.Any(e => e.FullMessage.Contains(formatted)))
            {
                var foundErrors = string.Join(", ", checkerDiagnostics);
                XAssert.Fail($"None of the found errors contains '{formatted}'.  Found errors: {foundErrors}");
            }

            return checkerDiagnostics;
        }

        /// <nodoc />
        public static void ExpectErrorCode(this TestResultBase testResult, int code, int count, string prefix = "DS")
        {
            Contract.Requires(testResult != null);
            Contract.Requires(count >= 0);

            int found = testResult.Diagnostics.Count(dsError => code == dsError.ErrorCode);

            var diagnostics = string.Join(Environment.NewLine, testResult.Diagnostics.Select(e => e.FullMessage));
            Assert.True(count == found,
               I($"Expect error code '{code}' to occur {count} time(s), but only occur {found} time(s). Available errors:\r\n{diagnostics}"));
        }

        /// <nodoc />
        public static void ExpectErrorCode(this IEnumerable<Diagnostic> diagnostics, global::BuildXL.FrontEnd.Script.Tracing.LogEventId code)
        {
            Contract.Requires(diagnostics != null);

            string allDiagnostics = string.Join(Environment.NewLine, diagnostics.Select(e => e.FullMessage));
            Assert.True(diagnostics.Any(d => d.ErrorCode == (int)code), 
               I($"Expect error code '{code}', but fail to found one. Available errors:\r\n{allDiagnostics}"));
        }
        
        /// <nodoc />
        public static void ExpectMessage(this IEnumerable<Diagnostic> diagnostics, string message)
        {
            Contract.Requires(diagnostics != null);

            string allDiagnostics = string.Join(Environment.NewLine, diagnostics.Select(e => e.Message));
            Assert.True(diagnostics.Any(d => d.Message.Contains(message)), 
               I($"Expect message '{message}', but fail to found one. Available errors:\r\n{allDiagnostics}"));
        }
    }
}
