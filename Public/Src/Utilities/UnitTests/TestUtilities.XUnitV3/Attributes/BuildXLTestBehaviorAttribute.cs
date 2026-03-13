// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using BuildXL.Utilities.Core;
using Xunit;
using Xunit.v3;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Provides BuildXL-specific test behaviors ported from the v2 custom TestFrameworkOverride.
    /// Apply at assembly level to affect all tests: [assembly: BuildXLTestBehavior]
    ///
    /// Features:
    /// - Clears xunit's SynchronizationContext so tests run with the default scheduler
    /// - Captures and restores Console.Out/Error to prevent tests from breaking console output
    /// - Hooks Contract.ContractFailed to detect contract violations during test execution
    /// - Fail-fast on contract violations for ContentStore tests (unless DisableFailFast trait)
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class BuildXLTestBehaviorAttribute : BeforeAfterTestAttribute
    {
        [ThreadStatic]
        private static TextWriter s_savedConsoleOut;

        [ThreadStatic]
        private static TextWriter s_savedConsoleError;

        [ThreadStatic]
        private static Dictionary<(string condition, string message), StackTrace> s_contractViolations;

        [ThreadStatic]
        private static bool s_failFastOnContractViolation;

        [ThreadStatic]
        private static EventHandler<ContractFailedEventArgs> s_contractHandler;

        /// <summary>
        /// Static constructor ensures ContentHashingUtilities is initialized before any tests run.
        /// In v2, this was done in the custom AssemblyRunner.
        /// Must happen in the static constructor (not Before) because some test types have
        /// static constructors that depend on ContentHashingUtilities being initialized.
        /// </summary>
        static BuildXLTestBehaviorAttribute()
        {
            global::BuildXL.Storage.ContentHashingUtilities.SetDefaultHashType();
        }

        /// <inheritdoc />
        public override void Before(MethodInfo methodUnderTest, IXunitTest test)
        {
            // Check class-level skip attribute. In v2, the custom ClassRunner handled this;
            // in v3, we check it here since we don't have a custom runner.
            try
            {
                var classSkipAttr = methodUnderTest.DeclaringType?
                    .GetCustomAttributes(typeof(TestClassIfSupportedAttribute), inherit: true);
                if (classSkipAttr != null)
                {
                    foreach (TestClassIfSupportedAttribute attr in classSkipAttr)
                    {
                        if (!string.IsNullOrEmpty(attr.Skip))
                        {
                            Assert.Skip(attr.Skip);
                        }
                    }
                }
            }
            catch (global::Xunit.Sdk.SkipException)
            {
                throw; // Let skip exceptions propagate normally
            }
            catch (Exception ex)
            {
                // If the attribute constructor throws (e.g., native P/Invoke failure when
                // checking requirements on the current platform), fail-safe by skipping.
                // The intent of TestClassIfSupportedAttribute is to skip unsupported tests,
                // so a failure to evaluate requirements should also result in a skip.
                Assert.Skip($"Test class requirements could not be evaluated: {ex}");
            }

            // Clear xunit's SynchronizationContext so tests run with the default scheduler.
            if (OperatingSystemHelper.IsWindowsOS)
            {
                SynchronizationContext.SetSynchronizationContext(null);
            }

            // Capture console streams so they can be restored after the test,
            // even if the test replaces them.
            s_savedConsoleOut = Console.Out;
            s_savedConsoleError = Console.Error;

            // Set up contract violation detection.
            s_contractViolations = new Dictionary<(string condition, string message), StackTrace>();
            s_failFastOnContractViolation = test.TestCase.TestCaseDisplayName.Contains("ContentStore")
                && !test.TestCase.Traits.ContainsKey("DisableFailFast");

            if (s_failFastOnContractViolation)
            {
                s_contractHandler = (sender, args) =>
                {
                    s_contractViolations[(args.Condition, args.Message)] = new StackTrace();
                };
                Contract.ContractFailed += s_contractHandler;
            }
        }

        /// <inheritdoc />
        public override void After(MethodInfo methodUnderTest, IXunitTest test)
        {
            try
            {
                // Restore console streams.
                if (s_savedConsoleOut != null)
                {
                    Console.SetOut(s_savedConsoleOut);
                }

                if (s_savedConsoleError != null)
                {
                    Console.SetError(s_savedConsoleError);
                }

                // Check for contract violations. Only fail if this test enabled fail-fast,
                // because Contract.ContractFailed is a global event — handlers installed by
                // ContentStore tests on other threads can leak and record violations for
                // non-ContentStore tests that intentionally trigger contract assertions
                // (e.g., via Assert.Throws).
                if (s_failFastOnContractViolation && s_contractViolations != null && s_contractViolations.Count != 0)
                {
                    string error = string.Join(", ", s_contractViolations.Select(
                        kvp => $"Condition: {kvp.Key.condition}, Message: {kvp.Key.message}, StackTrace: {kvp.Value}"));

                    // Limit the message length.
                    if (error.Length > 5000)
                    {
                        error = error.Substring(0, 5000) + "...";
                    }

                    throw new InvalidOperationException(
                        $"Contract exception(s) occurred when running {test.TestCase.TestCaseDisplayName}. {error}");
                }
            }
            finally
            {
                // Unhook the contract handler.
                if (s_contractHandler != null)
                {
                    Contract.ContractFailed -= s_contractHandler;
                    s_contractHandler = null;
                }

                s_contractViolations = null;
                s_savedConsoleOut = null;
                s_savedConsoleError = null;
            }
        }
    }
}
