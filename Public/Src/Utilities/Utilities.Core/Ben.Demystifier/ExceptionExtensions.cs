// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Collections.Generic.Enumerable;
using System.Reflection;
using BuildXL.Utilities.Core;

#nullable enable

namespace System.Diagnostics
{
    /// <nodoc />
    public static class ExceptionExtensions
    {
        private static readonly FieldInfo stackTraceString = typeof(Exception).GetField("_stackTraceString", BindingFlags.Instance | BindingFlags.NonPublic)!;

        /// <nodoc />
        private static T Demystify<T>(
            this T exception,
            Dictionary<Exception, string> originalStacks,
            bool rethrowException = false,
            Func<StackFrame, bool>? stackFrameFilter = null) where T : Exception
        {
            try
            {
                if (!originalStacks.ContainsKey(exception))
                {
                    originalStacks[exception] = (string)stackTraceString.GetValue(exception)!;
                }

                var stackTrace = new EnhancedStackTrace(exception, stackFrameFilter);

                if (stackTrace.FrameCount > 0)
                {
                    stackTraceString.SetValue(exception, stackTrace.ToString());
                }

                if (exception is AggregateException aggEx)
                {
                    foreach (var ex in EnumerableIList.Create(aggEx.InnerExceptions))
                    {
                        ex.Demystify(originalStacks);
                    }
                }

                exception.InnerException?.Demystify(originalStacks);
            }
#pragma warning disable ERP022 // Processing exceptions shouldn't throw exceptions; if it fails
            catch
            {
                if (rethrowException)
                {
                    throw;
                }
            }
#pragma warning restore ERP022

            return exception;
        }

        /// <nodoc />
        public static Exception Demystify(this Exception exception, bool rethrowException = false, Func<StackFrame, bool>? stackFrameFilter = null)
        {
            Analysis.IgnoreResult(exception.ToString(), "Need to trigger string computation first to materialized the stack trace");
            var originalStacks = new Dictionary<Exception, string>();
            exception.Demystify(originalStacks, rethrowException, stackFrameFilter); return exception;
        }
        
        /// <nodoc />
        public static string DemystifyToString(this Exception exception, bool rethrowException = false)
        {
            try
            {
                Analysis.IgnoreResult(exception.ToString(), "Need to trigger string computation first to materialized the stack trace");
                var originalStacks = new Dictionary<Exception, string>();
                exception.Demystify(originalStacks, rethrowException);

                string result = exception.ToString();

                foreach (var kvp in originalStacks)
                {
                    stackTraceString.SetValue(kvp.Key, kvp.Value);
                }

                return result;
            }
#pragma warning disable ERP022 // Processing exceptions shouldn't throw exceptions; if it fails
            catch
            {
                if (rethrowException)
                {
                    throw;
                }
            }
#pragma warning restore ERP022

            return exception.ToString();
        }
        
        /// <nodoc />
        public static string DemystifyStackTrace(this Exception exception, bool rethrowException = false)
        {
            try
            {
                Analysis.IgnoreResult(exception.ToString(), "Need to trigger string computation first to materialized the stack trace");
                var originalStacks = new Dictionary<Exception, string>();
                exception.Demystify(originalStacks);

                var result = exception.StackTrace;

                foreach (var kvp in originalStacks)
                {
                    stackTraceString.SetValue(kvp.Key, kvp.Value);
                }

                return result ?? string.Empty;
            }
#pragma warning disable ERP022 // Processing exceptions shouldn't throw exceptions; if it fails
            catch
            {
                if (rethrowException)
                {
                    throw;
                }
            }
#pragma warning restore ERP022
            return exception.ToString();
        }
    }
}
