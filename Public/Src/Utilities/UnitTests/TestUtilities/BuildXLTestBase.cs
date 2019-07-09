// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
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
        protected abstract void AssertAreEqual(int expected, int actual, string format, params object[] args);

        /// <nodoc/>
        protected void AssertAreEqual(int expected, int actual)
        {
            AssertAreEqual(expected, actual, string.Empty);
        }

        /// <nodoc/>
        protected abstract void AssertAreEqual(string expected, string actual, string format, params object[] args);

        /// <nodoc/>
        protected void AssertAreEqual(string expected, string actual)
        {
            AssertAreEqual(expected, actual, string.Empty);
        }

        /// <nodoc/>
        protected abstract void AssertTrue(bool condition, string format, params object[] args);

        /// <nodoc/>
        protected void AssertTrue(bool condition)
        {
            AssertTrue(condition, string.Empty);
        }

        /// <nodoc/>
        protected int m_expectedErrorCount;

        /// <nodoc/>
        protected int m_expectedWarningCount;

        /// <nodoc/>
        protected bool m_ignoreWarnings;
        private readonly Dictionary<int, int> m_expectedPerEventCounts = new Dictionary<int, int>();

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
        public const string TestPathRoot = @"Z:\TestPath";

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
        /// Event listener
        /// </summary>
        protected TestEventListenerBase m_eventListener;

        /// <summary>
        /// LoggingContext to be used by the test
        /// </summary>
        public LoggingContext LoggingContext { get; } = CreateLoggingContextForTest();

        /// <summary>
        /// Directory to use for test output. Uses the TestOutputDir environment variable in BuildXL builds to get
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
        /// Test event listener exposing event counts and messages logged during test execution.
        /// </summary>
        protected internal TestEventListenerBase EventListener => m_eventListener;

        /// <summary>
        /// Allows registration of additional event sources.
        /// </summary>
        protected void RegisterEventSource(EventSource eventSource)
        {
            if (eventSource.ConstructionException != null)
            {
                throw eventSource.ConstructionException;
            }

            m_eventListener.RegisterEventSource(eventSource);
        }

        /// <summary>
        /// When a test is expected to log failures (warnings or errors), one can call this method to establish the expected number
        /// of each, as well as optional message substrings to look for. Note that this method is mutually exclusive with using the
        /// assertion variants (e.g. <see cref="AssertErrorEventLogged(System.Enum,int)" />)
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
        /// Don't check for warnings.
        /// </summary>
        protected void IgnoreWarnings()
        {
            m_ignoreWarnings = true;
        }

        /// <summary>
        /// Asserts that the test's event listener has recorded <paramref name="count" /> additional instances
        /// of the given error event ID since the previous assertion (of the same event).
        /// Note that error without a matching assertion will fail the test at the end.
        /// </summary>
        protected void AssertErrorEventLogged(Enum eventId, int count = 1)
        {
            Contract.Requires(count >= 0);
            Contract.Requires(eventId != null, "Argument eventId cannot be null.");
            Contract.Requires(EventListener != null);
            m_expectedErrorCount += count;
            AssertEventLogged(eventId, count);
        }

        /// <summary>
        /// Allows the test's event listener to maybe record instances of the given error event ID since 
        /// the previous assertion (of the same event).
        /// This is useful when there is some expected non-deterministic error condition (due to a race, for example)
        /// </summary>
        protected void AllowErrorEventMaybeLogged(Enum eventId)
        {
            Contract.Requires(eventId != null, "Argument eventId cannot be null.");
            Contract.Requires(EventListener != null);

            var eventCount = EventListener.GetEventCount(Convert.ToInt32(eventId, CultureInfo.InvariantCulture));
            if (eventCount > 0)
            {
                m_expectedErrorCount += eventCount;
            }
        }

        /// <summary>
        /// Asserts that the test's event listener has recorded the expected number of errors,
        /// based on prior error assertions.
        /// This is used to ensure that no additional error have been logged.
        /// </summary>
        protected void AssertErrorCount()
        {
            AssertAreEqual(m_expectedErrorCount, EventListener.ErrorCount, "Mismatch in expected error count:\n" + EventListener.GetLog());
        }

        /// <summary>
        /// Asserts that the test's event listener has recorded the expected number of warnings,
        /// based on prior error assertions.
        /// This is used to ensure that no additional warning have been logged.
        /// </summary>
        protected void AssertWarningCount()
        {
            AssertAreEqual(m_expectedWarningCount, EventListener.WarningCount, "Mismatch in expected warning count:\n" + EventListener.GetLog());
        }

        /// <summary>
        /// Asserts that the test's event listener has recorded <paramref name="count" /> (or more, if <paramref name="allowMore"/>
        /// is set) additional instances of the given warning event ID since the previous assertion (of the same event).
        /// Note that warnings without a matching assertion will fail the test at the end.
        /// </summary>
        protected void AssertWarningEventLogged(Enum eventId, int count = 1, bool allowMore = false)
        {
            Contract.Requires(count >= 0);
            Contract.Requires(eventId != null, "Argument eventId cannot be null.");
            Contract.Requires(EventListener != null);
            m_expectedWarningCount += AssertEventLogged(eventId, count, allowMore);
        }

        /// <summary>
        /// Asserts that the test's event listener has recorded <paramref name="count" /> (or more, if <paramref name="allowMore"/>
        /// is set) additional instances of the given event ID since the previous assertion (of the same event).
        /// This method should not be used for events that are error or warning level, since their existence
        /// will fail the test at cleanup time. See e.g. <see cref="AssertErrorEventLogged(System.Enum,int)" />.  The result is the
        /// actual number of newly recorded events of the given ID.
        /// </summary>
        protected int AssertInformationalEventLogged(Enum eventId, int count = 1, bool allowMore = false)
        {
            Contract.Requires(count >= 0);
            Contract.Requires(eventId != null, "Argument eventId cannot be null.");
            Contract.Requires(EventListener != null);
            return AssertEventLogged(eventId, count, allowMore);
        }

        /// <summary>
        /// Asserts that the test's event listener has recorded <paramref name="count" /> (or more, if <paramref name="allowMore"/>
        /// is set) additional instances of the given event ID since the previous assertion (of the same event).
        /// This method should not be used for events that are error or warning level, since their existence
        /// will fail the test at cleanup time. See e.g. <see cref="AssertErrorEventLogged(System.Enum,int)" />.
        /// </summary>
        protected void AssertVerboseEventLogged(Enum eventId, int count = 1, bool allowMore = false)
        {
            Contract.Requires(count >= 0);
            Contract.Requires(eventId != null, "Argument eventId cannot be null.");
            Contract.Requires(EventListener != null);
            AssertEventLogged(eventId, count, allowMore);
        }

        /// <summary>
        /// Asserts that the test's event listener has recorded one or more instances
        /// of the given verbose-level event ID for the specified absolute path since the previous assertion (of the same event).
        /// This is applicable only to some file monitoring events that identify a path, but for which the number
        /// of occurrences is not easily predictable. The number of occurrences is returned on success (ensured positive).
        /// </summary>
        protected void AssertVerboseEventLogged(EventId eventId, string path)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));
            AssertEventLoggedWithPath(eventId, path);
        }

        /// <summary>
        /// Asserts that the test's event listener has recorded <paramref name="count" /> (or more, if <paramref name="allowMore"/>
        /// is set) additional instances of the given event ID since the previous assertion (of the same event). The result is the
        /// actual number of newly recorded events of the given ID.
        /// </summary>
        private int AssertEventLogged(Enum eventId, int count = 1, bool allowMore = false)
        {
            Contract.Requires(count >= 0);
            Contract.Requires(eventId != null, "Argument eventId cannot be null.");
            return AssertEventLogged(Convert.ToInt32(eventId, CultureInfo.InvariantCulture), eventId.ToString("G"), count, allowMore);
        }

        /// <summary>
        /// Asserts that the test's event listener has recorded <paramref name="count" /> (or more, if <paramref name="allowMore"/>
        /// is set) additional instances of the given event ID since the previous assertion (of the same event). The result is the
        /// actual number of newly recorded events of the given ID.
        /// </summary>
        private int AssertEventLogged(int eventId, string eventName = "<name unknown>", int count = 1, bool allowMore = false)
        {
            Contract.Requires(!string.IsNullOrEmpty(eventName));
            Contract.Requires(count >= 0);

            int actualEventCountLogged = EventListener.GetEventCount(eventId);
            m_expectedPerEventCounts.TryGetValue(eventId, out int currentCount);
            int newCount = actualEventCountLogged - currentCount;

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
        /// Asserts that the test's event listener has recorded one or more instances
        /// of the given event ID for the specified absolute path since the previous assertion (of the same event).
        /// This is applicable only to some file monitoring events that identify a path, but for which the number
        /// of occurrences is not easily predictable. The number of occurrences is returned on success (ensured positive).
        /// </summary>
        private int AssertEventLoggedWithPath(EventId eventId, string path)
        {
            int newOccurrencesForPath = EventListener.GetAndResetEventCountForPath(eventId, path);
            AssertTrue(
                newOccurrencesForPath != 0,
                "The event '{0:G}' (id: {1}) for path {2} should have been logged exactly one or more additional times since it was last checked.",
                eventId,
                (int)eventId,
                path);

            int expectedEventCountLogged;
            m_expectedPerEventCounts.TryGetValue((int)eventId, out expectedEventCountLogged);
            expectedEventCountLogged += newOccurrencesForPath;
            m_expectedPerEventCounts[(int)eventId] = expectedEventCountLogged;

            return newOccurrencesForPath;
        }

        /// <summary>
        /// Verifies that the test's event trace contains the given strings.
        /// </summary>
        /// <param name="caseSensitive">true if a case sensitive search should be done</param>
        /// <param name="requiredLogMessages">A list of substrings that are expected to have been logged.</param>
        protected void AssertLogContains(bool caseSensitive, params string[] requiredLogMessages)
        {
            Contract.Requires(requiredLogMessages != null);
            AssertLogWithContainmentExpectation(caseSensitive, true, requiredLogMessages);
        }

        /// <summary>
        /// Verifies that the test's event trace does not contain the given strings.
        /// </summary>
        /// <param name="caseSensitive">true if a case sensitive search should be done</param>
        /// <param name="requiredLogMessages">A list of substrings that are expected to have not been logged.</param>
        protected void AssertLogNotContains(bool caseSensitive, params string[] requiredLogMessages)
        {
            Contract.Requires(requiredLogMessages != null);
            AssertLogWithContainmentExpectation(caseSensitive, false, requiredLogMessages);
        }

        private void AssertLogWithContainmentExpectation(bool caseSensitive, bool expectToContain, params string[] requiredLogMessages)
        {
            Contract.Requires(requiredLogMessages != null);
            string originalLog = EventListener.GetLog();
            string log = caseSensitive ? originalLog : originalLog.ToUpperInvariant();
            foreach (string requiredSubString in requiredLogMessages)
            {
                string searchString = caseSensitive ? requiredSubString : requiredSubString.ToUpperInvariant();
                Contract.Assume(searchString != null);

                if (expectToContain)
                {
                    if (!log.Contains(searchString))
                    {
                        AssertTrue(
                            false,
                            "Did not find substring '{0}' in the output log:\n\r----------\n\r{1}\n\r----------\n\r",
                            requiredSubString,
                            originalLog);
                    }
                }
                else
                {
                    if (log.Contains(searchString))
                    {
                        AssertTrue(
                            false,
                            "Found substring '{0}' in the output log:\n\r----------\n\r{1}\n\r----------\n\r",
                            requiredSubString,
                            originalLog);
                    }
                }
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

        /// <summary>
        /// Validates warning and error counts
        /// </summary>
        protected void ValidateWarningsAndErrors()
        {
            AssertAreEqual(m_expectedErrorCount, EventListener.ErrorCount, "Mismatch in expected error count:\n" + EventListener.GetLog());
            if (!m_ignoreWarnings)
            {
                // Need to check whether any of the log lines contain an ignored warning and handle appropriately
                int ignoredWarningCount = 0;
                string log = EventListener.GetLog();
                var modifiedLog = new StringBuilder();
                foreach (string line in log.Split(new[] { Environment.NewLine }, StringSplitOptions.None))
                {
                    if (ShouldWarningBeIgnored(line))
                    {
                        ignoredWarningCount++;
                    }
                    else
                    {
                        modifiedLog.AppendLine(line);
                    }
                }

                AssertAreEqual(
                    m_expectedWarningCount,
                    EventListener.WarningCount - ignoredWarningCount,
                    "Mismatch in expected warning count:\n" + modifiedLog);
            }

            if (m_requiredLogMessageSubStrings != null)
            {
                AssertLogContains(m_caseSensitiveSearch, m_requiredLogMessageSubStrings);
            }
        }

        /// <summary>
        /// Gets a testData value that is specified in the build specification
        /// </summary>
        /// <remarks>
        /// To have value 'myTestValue' accessible you can use:
        /// const dll = BuildXLSdk.test({
        ///     // other args
        ///     runTestArgs: {
        ///         testRunData: {
        ///             myTestValue: "xyz",
        ///         },
        ///     }
        /// });
        /// </remarks>
        protected string GetTestDataValue(string key)
        {
            var testData = this.GetTestData();
            if (!testData.TryGetValue(key, out var result))
            {
                AssertTrue(false, "TestData does not contain value: {0}", key);
            }

            return result;
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
    }
}
