// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Utilities;
using JetBrains.Annotations;
using Xunit;

using static BuildXL.Utilities.FormattableStringEx;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Convenience wrappers for xunit asserts to match mstest style asserts
    /// </summary>
    public static class XAssert
    {
        private static readonly Regex s_whitespacesRegex = new Regex(@"[\s\t\n\r]+");

        private static string GetMessage(string format, params object[] args)
        {
            if (args.Length > 0)
            {
                return string.Format(CultureInfo.InvariantCulture, format, args);
            }

            return format;
        }

        private static string GetEqualDebugString<T>(T expected, T actual)
        {
            // ToString() might throw for the objects. If it does, we won't print the values
            return "Expected: " + ToString(expected) + " Actual: " + ToString(actual) + " ";
        }

        private static string ToString<T>(T value)
        {
            try
            {
                return value == null ? "null" : value.ToString();
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                return "{eval-error}";
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        private static string ArrayToString<T>(T[] value)
        {
            return value == null
                ? "null"
                : "[" + string.Join(", ", value.Select(ToString)) + "]";
        }

        /// <nodoc/>
        public static string SetToString<T>(IEnumerable<T> value)
        {
            return value == null
                ? "null"
                : "{" + string.Join(", ", value.Select(ToString)) + "}";
        }

        /// <nodoc/>
        public static void AreEqual<T>(T expected, T actual)
        {
            Assert.Equal(expected, actual);
        }

        /// <nodoc/>
        public static void AreEqual(object expected, object actual)
        {
            Assert.Equal(expected, actual);
        }

        /// <nodoc/>
        [StringFormatMethod("format")]
        public static void AreEqual<T>(T expected, T actual, string format, params object[] args)
        {
            if (expected == null)
            {
                Assert.True(actual == null, GetEqualDebugString(expected, actual) + GetMessage(format, args));
            }
            else
            {
                Assert.True(
                    expected.Equals(actual),
                    GetEqualDebugString(expected, actual) + GetMessage(format, args));
            }
        }

        /// <summary>
        /// Asserts that any exception is thrown.  Returns thrown exception.
        /// </summary>
        public static Task<Exception> ThrowsAnyAsync(Func<Task> actionAsync) => ThrowsAnyAsync<Exception>(actionAsync);

        /// <summary>
        /// Non-async version of <see cref="ThrowsAnyAsync(Func{Task})"/>.
        /// </summary>
        public static Exception ThrowsAny(Action action) => ThrowsAnyAsync(ToAsyncFunc(action)).GetAwaiter().GetResult();

        /// <summary>
        /// Syntactic sugar for <see cref="ThrowsEitherAsync(Func{Task}, Type[])"/>.
        /// </summary>
        public static Task<Exception> ThrowsEitherAsync<T1, T2>(Func<Task> actionAsync) => ThrowsEitherAsync(actionAsync, typeof(T1), typeof(T2));

        /// <summary>
        /// Asserts that an exception is thrown and that it is of either of the given types.
        /// </summary>1
        public static async Task<Exception> ThrowsEitherAsync(Func<Task> actionAsync, params Type[] expectedTypes)
        {
            var ex = await ThrowsAnyAsync(actionAsync);
            IsTrue(
                expectedTypes.Any(t => t.IsAssignableFrom(ex.GetType())),
                "Thrown exception is of type {0}, but was expected to be either [{1}]",
                ex.GetType(),
                string.Join(", ", expectedTypes.Select(t => t.FullName)));
            return ex;
        }

        /// <summary>
        /// Asserts that any exception of type {T} is thrown.
        /// </summary>
        public static async Task<T> ThrowsAnyAsync<T>(Func<Task> actionAsync) where T : Exception
        {
            var ex = await CatchAny(actionAsync);
            IsNotNull(ex, ". Expected exception of type {0} to be thrown.", typeof(T).FullName);
            IsTrue(typeof(T).IsAssignableFrom(ex.GetType()), "Expected caught exception to be of type {0}, but is {1}", typeof(T), ex.GetType());
            return (T)ex;
        }

        private static Func<Task> ToAsyncFunc(Action action)
        {
            return new Func<Task>(() =>
            {
                action();
                return Task.FromResult(42);
            });
        }

        /// <summary>
        /// Catches and returns any exception.  If no exception is thrown, null is returned.
        /// </summary>
        private static async Task<Exception> CatchAny(Func<Task> actionAsync)
        {
            try
            {
                await actionAsync();
                return null;
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch (Exception e)
            {
                return e;
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        /// <nodoc/>
        [StringFormatMethod("format")]
        public static void AreEqual(object expected, object actual, string format, params object[] args)
        {
            if (expected == null)
            {
                Assert.True(actual == null, GetEqualDebugString(expected, actual) + GetMessage(format, args));
            }
            else
            {
                Assert.True(
                    expected.Equals(actual),
                    GetEqualDebugString(expected, actual) + GetMessage(format, args));
            }
        }

        /// <nodoc/>
        [StringFormatMethod("format")]
        public static void ArrayEqual<T>(T[] expected, T[] actual) => AreArraysEqual(expected, actual, true);

        /// <nodoc/>
        [StringFormatMethod("format")]
        public static void ArrayNotEqual<T>(T[] expected, T[] actual) => AreArraysEqual(expected, actual, false);

        /// <nodoc/>
        [StringFormatMethod("format")]
        public static void SetEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T> comparer = null) => AreSetsEqual(expected, actual, true, comparer);

        /// <nodoc/>
        [StringFormatMethod("format")]
        public static void SetNotEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T> comparer = null) => AreSetsEqual(expected, actual, false, comparer);

        /// <nodoc/>
        public static void AreArraysEqual<T>(T[] expected, T[] actual, bool expectedResult, string format = null, params object[] args)
        {
            var notEqualDescription = CheckIfArraysAreEqual(expected, actual);
            var actualResult = notEqualDescription == null;
            if (expectedResult != actualResult)
            {
                var equalityMessage = notEqualDescription ?? "Expected arrays not to be equal, but they are.";
                var userMessage = format != null
                    ? GetMessage(format, args) + Environment.NewLine + equalityMessage
                    : equalityMessage;
                throw new global::Xunit.Sdk.AssertActualExpectedException(
                    ArrayToString(expected),
                    ArrayToString(actual),
                    userMessage);
            }
        }

        /// <nodoc/>
        public static void AreSetsEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, bool expectedResult, IEqualityComparer<T> comparer = null, string format = null, params object[] args)
        {
            var expectedSet = expected != null ? new HashSet<T>(expected) : null;
            var actualSet = actual != null ? new HashSet<T>(actual) : null;
            var notEqualDescription = CheckIfSetsAreEqual(expectedSet, actualSet, comparer);
            var actualResult = notEqualDescription == null;
            if (expectedResult != actualResult)
            {
                var equalityMessage = notEqualDescription ?? "Expected sets not to be equal, but they are.";
                var userMessage = format != null
                    ? GetMessage(format, args) + Environment.NewLine + equalityMessage
                    : equalityMessage;
                throw new global::Xunit.Sdk.AssertActualExpectedException(
                    SetToString(expectedSet),
                    SetToString(actualSet),
                    userMessage);
            }
        }

        private static string CheckIfArraysAreEqual<T>(T[] expected, T[] actual)
        {
            Func<int> firstDisagreeingIndex = () =>
            {
                var disagreeingIndexes = Enumerable
                    .Range(0, expected.Length)
                    .Where(x => !Equals(expected[x], actual[x]));
                return disagreeingIndexes.Any() ? disagreeingIndexes.First() : -1;
            };
            int i = -1;
            return
                expected == null && actual == null ? null :
                expected == null && actual != null ? "Expected null but got a non-null array" :
                expected != null && actual == null ? "Expected a non-null array but got null" :
                expected.Length != actual.Length ? "Array lengths are different" :
                (i = firstDisagreeingIndex()) != -1 ? "Elements at position " + i + " disagree" :
                null;
        }

        private static string CheckIfSetsAreEqual<T>(HashSet<T> expected, HashSet<T> actual, IEqualityComparer<T> comparer = null)
        {
            comparer = comparer ?? EqualityComparer<T>.Default;
            Func<List<T>> missingElems = () => expected.Where(e => !actual.Contains(e, comparer)).ToList();
            List<T> missing;
            return
                expected == null && actual == null ? null :
                expected == null && actual != null ? "Expected null but got a non-null set" :
                expected != null && actual == null ? "Expected a non-null set but got null" :
                expected.Count != actual.Count ? "Set sizes are different" :
                (missing = missingElems()).Any() ? "Missing elements: " + SetToString(missing) :
                null;
        }

        /// <nodoc/>
        public static void AreNotEqual<T>(T expected, T actual)
        {
            Assert.NotEqual(expected, actual);
        }

        /// <nodoc/>
        public static void AreNotEqual(object expected, object actual)
        {
            Assert.NotEqual(expected, actual);
        }

        /// <nodoc/>
        [StringFormatMethod("format")]
        public static void AreNotEqual<T>(T expected, T actual, string format, params object[] args)
        {
            Assert.False(
                expected.Equals(actual),
                "Expected not equal: " + expected + " Actual: " + actual + " " +
                GetMessage(format, args));
        }

        /// <nodoc/>
        [StringFormatMethod("format")]
        public static void AreNotEqual(object expected, object actual, string format, params object[] args)
        {
            Assert.False(
                expected.Equals(actual),
                "Expected not equal: " + expected + " Actual: " + actual + " " +
                GetMessage(format, args));
        }

        /// <nodoc/>
        public static void IsTrue(bool condition, string format, params object[] args)
        {
            Assert.True(
                condition,
                GetMessage(format, args));
        }

        /// <nodoc/>
        public static void IsTrue(bool condition)
        {
            Assert.True(condition);
        }

        /// <nodoc/>
        public static void IsFalse(bool condition, string format, params object[] args)
        {
            Assert.False(
                condition,
                GetMessage(format, args));
        }

        /// <nodoc/>
        public static void IsFalse(bool condition)
        {
            Assert.False(condition);
        }

        /// <nodoc/>
        public static void Contains<T>(IEnumerable<T> container, params T[] elems)
        {
            foreach (var elem in elems)
            {
                if (!container.Contains(elem))
                {
                    Assert.True(false, I($"Element '{elem}' not found in container: {RenderContainer(container)}"));
                }
            }
        }

        /// <nodoc/>
        public static void ContainsNot<T>(IEnumerable<T> container, params T[] elems)
        {
            foreach (var elem in elems)
            {
                if (container.Contains(elem))
                {
                    Assert.True(false, I($"Element '{elem}' found in container: {RenderContainer(container)}"));
                }
            }
        }

        private static string RenderContainer<T>(IEnumerable<T> container)
        {
            string nl = Environment.NewLine;
            var elems = container.Select(e => I($"  '{e}'"));
            return I($"[{nl}{string.Join("," + nl, elems)}{nl}]");
        }

        /// <nodoc/>
        [StringFormatMethod("format")]
        public static void Fail(string format, params object[] args)
        {
            Assert.True(
                false,
                GetMessage(format, args));
        }

        /// <nodoc/>
        public static void Fail()
        {
            Assert.True(false);
        }

        /// <nodoc/>
        public static void AreSame(object expected, object actual)
        {
            Assert.Same(expected, actual);
        }

        /// <nodoc/>
        [StringFormatMethod("format")]
        public static void AreSame(object expected, object actual, string format, params object[] args)
        {
            Assert.True(
                object.ReferenceEquals(expected, actual),
                "Objects expected to be the same" +
                GetMessage(format, args));
        }

        /// <nodoc/>
        public static void AreNotSame(object expected, object actual)
        {
            Assert.NotSame(expected, actual);
        }

        /// <nodoc/>
        [StringFormatMethod("format")]
        public static void AreNotSame(object expected, object actual, string format, params object[] args)
        {
            Assert.True(
                !object.ReferenceEquals(expected, actual),
                "Objects expected to not be the same" +
                GetMessage(format, args));
        }

        /// <nodoc/>
        public static void IsNotNull(object value)
        {
            Assert.NotNull(value);
        }

        /// <nodoc/>
        [StringFormatMethod("format")]
        public static void IsNotNull(object value, string format, params object[] args)
        {
            Assert.True(
                value != null,
                "Expected value to not be null" +
                GetMessage(format, args));
        }

        /// <nodoc/>
        public static void IsNull(object value)
        {
            Assert.Null(value);
        }

        /// <nodoc/>
        [StringFormatMethod("format")]
        public static void IsNull(object value, string format, params object[] args)
        {
            Assert.True(
                value == null,
                "Expected object to be null" +
                GetMessage(format, args));
        }

        /// <nodoc/>
        public static void EqualIgnoreWhiteSpace(string expected, string actual, bool ignoreCase = false)
        {
            Assert.Equal(s_whitespacesRegex.Replace(expected, string.Empty), s_whitespacesRegex.Replace(actual, string.Empty), ignoreCase);
        }
        
        /// <nodoc/>
        public static void ArePathEqual(string expected, string actual)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                AreEqual(expected, actual);
            }
            else
            {
                AreEqual(expected.ToUpperInvariant(), actual.ToUpperInvariant());
            }
        }

        /// <nodoc/>
        public static void PossiblySucceeded<T>(Possible<T> result, string message = null)
        {
            if (!result.Succeeded)
            {
                string failureMessage = result.Failure.DescribeIncludingInnerFailures();
                if (message == null)
                {
                    message = failureMessage;
                }
                else
                {
                    message += Environment.NewLine + failureMessage;
                }
            }

            XAssert.IsTrue(result.Succeeded, message);
        }
    }
}
