// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;

namespace BuildXL.FrontEnd.Script.Core
{
    /// <summary>
    /// Set of operations available on <see cref="Number"/> type in DScript.
    /// </summary>
    internal static class NumberOperations
    {
        /// <summary>
        /// Regular right shift operator (>>).
        /// This operator shifts the first operand the specified number of bits to the right.
        /// Excess bits shifted off to the right are discarded.
        /// Copies of the leftmost bit are shifted in from the left.
        /// Since the new leftmost bit has the same value as the previous leftmost bit, the sign bit (the leftmost bit) does not change.
        /// Hence the name "sign-propagating".
        /// (from https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Bitwise_Operators)
        /// </summary>
        public static Number SignPropagatingRightShift(Number a, Number b)
        {
            Contract.Requires(a.IsValid);
            Contract.Requires(b.IsValid);

            return a.Value >> b.Value;
        }

        /// <summary>
        /// Unsigned right shift operator (>>>).
        /// This operator shifts the first operand the specified number of bits to the right.
        /// Excess bits shifted off to the right are discarded. Zero bits are shifted in from the left.
        /// The sign bit becomes 0, so the result is always non-negative.
        /// (from https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Operators/Bitwise_Operators)
        /// </summary>
        /// <exception cref="System.OverflowException">
        /// Exception could be generated if the result will not fit into 32bit signed integer. For instance, -1 >>> 0 will generate this error.
        /// </exception>
        public static Number ZeroFillingRightShift(Number a, Number b)
        {
            Contract.Requires(a.IsValid);
            Contract.Requires(b.IsValid);

            var bValue = b.Value;

            // For zero filling right shift we need to set to 0
            // all b number of most significant bits.

            // Cast should be in unchecked context: (uint)-8 should be ok, but not a failure.
            uint aValue = unchecked((uint)a.Value);

            // Using '>>' to get zero filling behavior. Cast to uint is enough because '>>' has required behavior for unsigned ints.
            aValue = aValue >> bValue;

            // Conversion should happen in checked context, because
            // -1 >> 0 will provide uint.MaxValue that doesn't have representation in the int.
            return checked((int)aValue);
        }

        /// <summary>
        /// Returns specified number raised to specified power.
        /// Performs required conversion to numbers.
        /// </summary>
        public static EvaluationResult Power(ImmutableContextBase context, int left, int right, Location location)
        {
            if (right < 0)
            {
                context.Logger.ReportArgumentForPowerOperationShouldNotBeNegative(context.LoggingContext, location);
                return EvaluationResult.Error;
            }

            return EvaluationResult.Create(checked((int)Math.Pow(left, right)));
        }
    }
}
