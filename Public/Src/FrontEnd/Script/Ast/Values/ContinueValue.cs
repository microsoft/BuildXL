// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
