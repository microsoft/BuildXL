// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;

namespace BuildXL.Processes.Sideband
{
    /// <nodoc />
    internal static class SidebandUtils
    {
        /// <nodoc />
        internal static void AssertOrder(ref int counter, Func<int, bool> condition, string errorMessage)
        {
            var currentValue = Interlocked.Increment(ref counter);
            if (!condition(currentValue))
            {
                throw Contract.AssertFailure($"{errorMessage}. Current counter: " + currentValue);
            }
        }
    }
}
