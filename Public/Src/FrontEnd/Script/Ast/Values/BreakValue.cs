// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Break value.
    /// </summary>
    public sealed class BreakValue
    {
        /// <summary>
        /// Instance of break value.
        /// </summary>
        public static BreakValue Instance { get; } = new BreakValue();

        /// <nodoc />
        private BreakValue()
        {
        }
    }
}
