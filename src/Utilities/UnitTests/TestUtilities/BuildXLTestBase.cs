// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using BuildXL.Native.Streams;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#endif
#if !FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using System.Diagnostics.Tracing;
#endif

namespace Test.BuildXL.TestUtilities
{
    /// <summary>
    /// Base class that sends all events to the console; tremendously useful to diagnose remote test run failures
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable",
        Justification = "Test follow different pattern with Initialize and Cleanup.")]
    public abstract class BuildXLTestBase
    {
        private Dictionary<string, string> m_testData;

        /// <nodoc/>
        protected long m_expectedErrorCount;

        /// <nodoc/>
        protected long m_expectedWarningCount;

        /// <nodoc/>
        protected abstract void AssertAreEqual(long expected, long actual, string format, params object[] args);

        /// <nodoc/>
        protected virtual void AssertAreEqual(int expected, int actual)
        {
            AssertAreEqual(expected, actual, string.Empty);
        }

        /// <nodoc/>
        protected abstract void AssertAreEqual(string expected, string actual, string format, params object[] args);

        /// <nodoc/>
        protected virtual void AssertAreEqual(string expected, string actual)
        {
            AssertAreEqual(expected, actual, string.Empty);
        }

        /// <nodoc/>
        protected abstract void AssertTrue(bool condition, string format, params object[] args);

        /// <nodoc/>
        protected virtual void AssertTrue(bool condition)
        {
            AssertTrue(condition, string.Empty);
        }

        /// <nodoc/>
        protected bool m_ignoreWarnings;
        private readonly Dictionary<int, long> m_expectedPerEventCounts = new Dictionary<int, long>();

        /// <nodoc/>
        protected string[] m_requiredLogMessageSubStrings;

        /// <nodoc/>
        protected bool m_caseSensitiveSearch;
        private string m_testOutputDirectory = null;

        /// <summary>
        /// Hook for the default <see cref="IOCompletionTraceHook"/> to ensure that any I/O started by a test is finished before the test exits.
        /// </summary>
        protected IOCompletionTraceHook m_ioCompletionTraceHook;

        /// <summary>
        /// Helper value for using as a path in tests.
        /// </summary>
        public const string TestPathRoot = @"Z:\BuildXLTestPath";

        /// <summary>
        /// Prefixes of warnings that may appear in the error log but will be ignored when checking that expected/actual
        /// warning counts match up. These will also be removed from the error log displayed when errors are displayed
        /// after a failed test
        /// </summary>
        private readonly List<string> m_warningIgnorePrefixes = new List<string>();

        /// <summary>
        /// Flag set when the [TestInitialize] method is called by the test runner. We track this to ensure that
        /// on [TestCleanup], it was set. If not, the inheritor isn't calling up correctly.
        /// </summary>
        protected bool m_initialized = false;

        /// <summary>
        /// Directory to use for test output. Uses the BuildXLTestOutputDir environment variable in BuildXL builds to get
        /// a relatively short path to avoid max path issues. When unset returns TestContext.TestRunDirectory
        /// </summary>
        public string TestOutputDirectory
        {
            get
            {
                if (m_testOutputDirectory == null)
                {
                    m_testOutputDirectory = Environment.GetEnvironmentVariable("TEMP");
                    Directory.CreateDirectory(m_testOutputDirectory);
                }

                return m_testOutputDirectory;
            }
        }

        /// <summary>
        /// Directory where test is running from
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic",
            Justification = "Allow consumers to call it as though it is a member to preserve usage from old TestBase")]
        public string TestDeploymentDir => Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(typeof(BuildXLTestBase).GetTypeInfo().Assembly));

        /// <summary>
        /// Don't check for warnings.
        /// </summary>
        protected void IgnoreWarnings()
        {
            m_ignoreWarnings = true;
        }

        /// <summary>
        /// Ignores warning DX0222 for a specific directory. This is useful if a test accesses files from a known root
        /// but should still fail for other warnings
        /// </summary>
        /// <param name="root">Absolute path to the root to ignore</param>
        protected void IgnoreNonSourceDirectoryAccessWarning(string root)
        {
            m_warningIgnorePrefixes.Add("Warning DX0222: The file '" + root);
        }

        /// <summary>
        /// Asserts that the two strings are equal
        /// </summary>
        protected void AssertStringEquals(string expected, object actual, bool ignoreCase = false)
        {
            var actualString = actual as string;
            if (actualString != null && expected != null)
            {
                var length = Math.Max(expected.Length, actualString.Length);
                for (int i = 0; i < length; i++)
                {
                    char expectedChar = i < expected.Length ? expected[i] : '\0';
                    char actualChar = i < actualString.Length ? actualString[i] : '\0';

                    if (ignoreCase ?
                        char.ToUpperInvariant(expectedChar) != char.ToUpperInvariant(actualChar) :
                        expectedChar != actualChar)
                    {
                        StringBuilder builder = new StringBuilder();
                        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "Strings differ at character '{0}'", i));
                        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "Expected character: '{0}'", expectedChar));
                        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "Actual character:   '{0}'", actualChar));
                        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "Expected string: <{0}>", expected));
                        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "Actual string:   <{0}>", actualString));
                        AssertTrue(false, builder.ToString());
                    }
                }

                AssertAreEqual(expected.Length, actualString.Length, string.Empty);
            }
            else
            {
                AssertTrue(false, "Expected {0}, Actual {1}", expected ?? "null", actual ?? null);
            }
        }

        /// <summary>
        /// Returns true if a warning string should be ignored
        /// </summary>
        private bool ShouldWarningBeIgnored(string warningLine)
        {
            foreach (string ignoreWarning in m_warningIgnorePrefixes)
            {
                if (warningLine.StartsWith(ignoreWarning, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // ignore warning related to the existence of experimental options
            if (warningLine.Contains("Warning DX0909:"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Creates a LoggingContext for use by tests
        /// </summary>
        public static LoggingContext CreateLoggingContextForTest()
        {
            return new LoggingContext(loggerComponentInfo: "BuildXLTest", environment: "BuildXLTest");
        }

        private Dictionary<string, string> GetTestData()
        {
            if (m_testData != null)
            {
                return m_testData;
            }

            var testDataFile = Environment.GetEnvironmentVariable("TestRunData");
            AssertTrue(!string.IsNullOrEmpty(testDataFile), "This test requires TestData, no testData environment variable was found");
            AssertTrue(File.Exists(testDataFile), "This testData file at '{0}' does not exist", testDataFile);
            XDocument testDataXml = null;
            try
            {
                testDataXml = XDocument.Load(testDataFile);
            }
            catch (IOException e)
            {
                AssertTrue(false, "Failed to load testData file '{0}': {1}", testDataFile, e);
            }
            catch (XmlException e)
            {
                AssertTrue(false, "Failed to load testData file '{0}': {1}", testDataFile, e);
            }
            catch (UnauthorizedAccessException e)
            {
                AssertTrue(false, "Failed to load testData file '{0}': {1}", testDataFile, e);
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var rootElement = testDataXml.Element("TestRunData");
            AssertTrue(rootElement != null, "Unexpected xml content");

            foreach (var entry in rootElement.Elements("Entry"))
            {
                var key = entry.Element("Key")?.Value;
                AssertTrue(!string.IsNullOrEmpty(key), "Unexpected xml content");

                var value = entry.Element("Value")?.Value;
                AssertTrue(!string.IsNullOrEmpty(value), "Unexpected xml content");

                result.Add(key, value);
            }

            m_testData = result;
            return m_testData;
        }

        /// <summary>
        /// Asserts that the test's event listener has recorded <paramref name="count" /> additional instances
        /// of the given error event ID since the previous assertion (of the same event).
        /// Note that error without a matching assertion will fail the test at the end.
        /// </summary>
        protected void AssertErrorEventLogged(LoggingContext context, Enum eventId, int count = 1)
        {
            Contract.Requires(count >= 0);
            Contract.Requires(eventId != null, "Argument eventId cannot be null.");
            m_expectedErrorCount += count;
            AssertEventLogged(context, eventId, count);
        }

        /// <summary>
        /// Asserts that the test's event listener has recorded <paramref name="count" /> (or more, if <paramref name="allowMore"/>
        /// is set) additional instances of the given event ID since the previous assertion (of the same event). The result is the
        /// actual number of newly recorded events of the given ID.
        /// </summary>
        private long AssertEventLogged(LoggingContext context, Enum eventId, int count = 1, bool allowMore = false)
        {
            Contract.Requires(count >= 0);
            Contract.Requires(eventId != null, "Argument eventId cannot be null.");
            return AssertEventLogged(context, Convert.ToInt32(eventId, CultureInfo.InvariantCulture), eventId.ToString("G"), count, allowMore);
        }

        /// <summary>
        /// Asserts that the test's event listener has recorded <paramref name="count" /> (or more, if <paramref name="allowMore"/>
        /// is set) additional instances of the given event ID since the previous assertion (of the same event). The result is the
        /// actual number of newly recorded events of the given ID.
        /// </summary>
        private long AssertEventLogged(LoggingContext context, int eventId, string eventName = "<name unknown>", long count = 1, bool allowMore = false)
        {
            Contract.Requires(!string.IsNullOrEmpty(eventName));
            Contract.Requires(count >= 0);

            long actualEventCountLogged = 0;
            context.EventsLoggedById.TryGetValue(eventId, out actualEventCountLogged);

            m_expectedPerEventCounts.TryGetValue(eventId, out long currentCount);
            long newCount = actualEventCountLogged - currentCount;

            if (allowMore)
            {
                AssertTrue(
                    newCount >= count,
                    "Expected event {0} to have been logged at least one more time, but it wasn't (current count: {1})",
                    eventId.ToString("G"), currentCount);
                count = newCount;
            }

            var expectedEventCountLogged = currentCount + count;
            m_expectedPerEventCounts[eventId] = expectedEventCountLogged;

            AssertAreEqual(
                expectedEventCountLogged,
                actualEventCountLogged,
                "The event '{0}' (id: {1}) should have been logged exactly {2} additional times since it was last checked. " +
                "It now should have been logged {3} times in total, but instead has been logged {4} times.",
                eventName,
                eventId,
                count,
                expectedEventCountLogged,
                actualEventCountLogged);

            return count;
        }

        /// <summary>
        /// When a test is expected to log failures (warnings or errors), one can call this method to establish the expected number
        /// of each, as well as optional message substrings to look for. Note that this method is mutually exclusive with using the
        /// assertion variants (e.g. <see cref="AssertErrorEventLogged" />)
        /// </summary>
        /// <param name="expectedErrorCount">The exact number expected of errors</param>
        /// <param name="expectedWarningCount">The exact number of expected warning</param>
        /// <param name="requiredLogMessageSubstrings">Optional strings that must exist in the log entry.</param>
        protected void SetExpectedFailures(int expectedErrorCount, int expectedWarningCount, params string[] requiredLogMessageSubstrings)
        {
            Contract.Assume(
                m_expectedErrorCount == 0 && m_expectedWarningCount == 0,
                "Expected errors / warnings have already been indicated via assertions. SetExpectedFailures should not be mixed with its assertion variants.");
            m_expectedErrorCount = expectedErrorCount;
            m_expectedWarningCount = expectedWarningCount;
            m_requiredLogMessageSubStrings = requiredLogMessageSubstrings;
            m_caseSensitiveSearch = false;
        }

        /// <summary>
        /// Asserts that the test's event listener has recorded <paramref name="count" /> (or more, if <paramref name="allowMore"/>
        /// is set) additional instances of the given event ID since the previous assertion (of the same event).
        /// This method should not be used for events that are error or warning level, since their existence
        /// will fail the test at cleanup time. See e.g. <see cref="AssertErrorEventLogged" />.
        /// </summary>
        protected void AssertVerboseEventLogged(LoggingContext context, Enum eventId, int count = 1, bool allowMore = false)
        {
            Contract.Requires(count >= 0);
            Contract.Requires(eventId != null, "Argument eventId cannot be null.");
            AssertEventLogged(context, eventId, count, allowMore);
        }

        /// <summary>
        /// Asserts that the test's event listener has recorded <paramref name="count" /> (or more, if <paramref name="allowMore"/>
        /// is set) additional instances of the given event ID since the previous assertion (of the same event).
        /// This method should not be used for events that are error or warning level, since their existence
        /// will fail the test at cleanup time. See e.g. <see cref="AssertErrorEventLogged" />.  The result is the
        /// actual number of newly recorded events of the given ID.
        /// </summary>
        protected long AssertInformationalEventLogged(LoggingContext context, Enum eventId, int count = 1, bool allowMore = false)
        {
            Contract.Requires(count >= 0);
            Contract.Requires(eventId != null, "Argument eventId cannot be null.");
            return AssertEventLogged(context, eventId, count, allowMore);
        }

        /// <summary>
        /// Asserts that the test's event listener has recorded <paramref name="count" /> (or more, if <paramref name="allowMore"/>
        /// is set) additional instances of the given warning event ID since the previous assertion (of the same event).
        /// Note that warnings without a matching assertion will fail the test at the end.
        /// </summary>
        protected void AssertWarningEventLogged(LoggingContext context, Enum eventId, int count = 1, bool allowMore = false)
        {
            Contract.Requires(count >= 0);
            Contract.Requires(eventId != null, "Argument eventId cannot be null.");
            m_expectedWarningCount += AssertEventLogged(context, eventId, count, allowMore);
        }
    }
}
