// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
