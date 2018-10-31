// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Utilities for performing casts without having to worry about overflows
    /// </summary>
    public static class SafeConvert
    {
        /// <nodoc/>
        public static int ToInt32(double value)
        {
            checked
            {
                try
                {
                    return (int)value;
                }
                catch (OverflowException)
                {
                    return 0;
                }
            }
        }

        /// <nodoc/>
        public static long ToLong(double value)
        {
            checked
            {
                try
                {
                    return (long)value;
                }
                catch (OverflowException)
                {
                    return 0;
                }
            }
        }
    }
}
