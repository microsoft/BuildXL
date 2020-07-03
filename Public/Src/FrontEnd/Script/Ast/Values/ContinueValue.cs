// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Continue value.
    /// </summary>
    public sealed class ContinueValue
    {
        /// <summary>
        /// Instance of continue value.
        /// </summary>
        public static ContinueValue Instance { get; } = new ContinueValue();

        /// <nodoc />
        private ContinueValue()
        {
        }
    }
}
