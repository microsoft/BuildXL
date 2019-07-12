// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;
using BuildXL.Utilities;

namespace BuildXL.Cache.ImplementationSupport
{
    /// <summary>
    /// Wrapper to enable logging of BuildXL Failure instances to ETW.
    /// </summary>
    /// <remarks>
    /// Since the BuildXL Failure class does not natively need or support the fields needed to correctly log to a
    /// self-described ETW event, this wraps that class. While the Failure class could be attributed, the odds of getting
    /// every failure type correct and all fields over time is fairly low, so this pattern insulates the ETW callers
    /// from changes in the Failure class.
    /// </remarks>
    [EventData]
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes", Justification = "Struct is never compared, but passed to ETW to let it unwrap.")]
    public readonly struct FailureETWWrapper
    {
        private readonly Failure m_failure;

        /// <nodoc/>
        public FailureETWWrapper(Failure failure)
        {
            m_failure = failure;
        }

        /// <summary>
        /// Text description of the failure
        /// </summary>
        [EventField]
        public string FailureDescription
        {
            get
            {
                if (m_failure == null)
                {
                    return string.Empty;
                }

                return m_failure.Describe();
            }
        }
    }

    /// <summary>
    /// Extensions to translate into FailureETWWrappers
    /// </summary>
    /// <remarks>
    /// Not all classes / structs are objects we can reliably attribute for ETW logging as
    /// the possible set of them is unbounded and unpredictable, so we'll use a shim to enable late
    /// bound translation.
    /// </remarks>
    public static class FailureETWWrapperExtensions
    {
        /// <summary>
        /// Formats a failure to an object ETW can understand.
        /// </summary>
        /// <param name="failure">Instance to format.</param>
        /// <returns>Object ETW can log properly.</returns>
        public static FailureETWWrapper ToETWFormat(this Failure failure)
        {
            return new FailureETWWrapper(failure);
        }
    }
}
