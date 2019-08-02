// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Collections.Generic.Enumerable;
using System.Reflection;
using BuildXL.Utilities;

namespace System.Diagnostics
{
    /// <nodoc />
    public static class ExceptionExtentions
    {
        private static readonly FieldInfo stackTraceString = typeof(Exception).GetField("_stackTraceString", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <nodoc />
        private static T Demystify<T>(this T exception, Dictionary<Exception, string> originalStacks) where T : Exception
        {
            try
            {
                if (!originalStacks.ContainsKey(exception))
                {
                    originalStacks[exception] = (string)stackTraceString.GetValue(exception);
                }

                var stackTrace = new EnhancedStackTrace(exception);

                if (stackTrace.FrameCount > 0)
                {
                    stackTraceString.SetValue(exception, stackTrace.ToString());
                }

                var aggEx = exception as AggregateException;
                if (aggEx != null)
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
            }
#pragma warning restore ERP022

            return exception;
        }
        
        /// <nodoc />
        public static string DemystifyToString(this Exception exception)
        {
            try
            {
                Analysis.IgnoreResult(exception.ToString(), "Need to trigger string computation first to materialized the stack trace");
                var originalStacks = new Dictionary<Exception, string>();
                exception.Demystify(originalStacks);

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
            }
#pragma warning restore ERP022

            return exception.ToString();
        }
    }
}
