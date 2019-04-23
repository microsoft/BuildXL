// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Error value.
    /// </summary>
    public sealed class ErrorValue
    {
        /// <nodoc />
        internal static readonly ErrorValue InternalInstance = new ErrorValue();

        /// <summary>
        /// Use this instance when needing to throw / return an error
        /// </summary>
        /// <remarks>
        /// Do not use this to check if something is an error Please use <see cref="ErrorValueExtensios.IsErrorValue" /> for that.
        /// This will aid in debugging since we can place a breakpoint in the body of the Instance property to find when an error is reported first.
        /// </remarks>
        public static ErrorValue Instance
        {
            get
            {
                return InternalInstance;
            }
        }

        /// <nodoc />
        internal ErrorValue()
        {
        }
    }

    /// <nodoc />
    public static class ErrorValueExtensios
    {
        /// <summary>
        /// Helper to see if a value is an ErrorValue.
        /// </summary>
        /// <remarks>
        /// Please use this to check if something is an error rather than comparing values to the <see cref="ErrorValue.InternalInstance"/> property.
        /// This will aid in debugging since we can place a breakpoint in the body of the Instance property to find when an error is reported first.
        /// </remarks>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsErrorValue(this object o)
        {
            return o == ErrorValue.InternalInstance;
        }
    }
}
