// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Evaluator.Profiling
{
    /// <summary>
    /// Represents a profiled function call registered by the profiler decorator
    /// </summary>
    public sealed class ProfiledFunctionCall
    {
        /// <nodoc/>
        public ProfiledFunctionCall(string callsiteInvocation, LocationData callsiteLocation, long durationInclusive,
            string qualifier, int functionId, string functionName, string functionLocation)
        {
            Contract.Requires(qualifier != null);

            CallsiteLocation = callsiteLocation;
            DurationInclusive = durationInclusive;
            CallsiteInvocation = callsiteInvocation;
            FunctionId = functionId;
            Qualifier = qualifier;
            FunctionName = functionName;
            FunctionLocation = functionLocation;
        }

        /// <summary>
        /// Gets the location of the callsite
        /// </summary>
        public LocationData CallsiteLocation { get; }

        /// <summary>
        /// Gets the duration of the call (including nested calls)
        /// </summary>
        public long DurationInclusive { get; }

        /// <summary>
        /// Gets a string representation of the callsite invocation
        /// </summary>
        public string CallsiteInvocation { get; }

        /// <summary>
        /// Gets the Id that identifies the function being called. Calls to the same function should have the same function id
        /// </summary>
        public int FunctionId { get; }

        /// <summary>
        /// Gets a string representation of the function being called
        /// </summary>
        public string FunctionName { get; }

        /// <summary>
        /// Gets a string representation of the function location being called
        /// </summary>
        public string FunctionLocation { get; }

        /// <summary>
        /// Gets the current qualifier used for the call
        /// </summary>
        public string Qualifier { get; }
    }
}
