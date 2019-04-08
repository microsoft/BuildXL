// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.ToolSupport
{
    /// <summary>
    /// Special exception type used for command-line argument validation.
    /// </summary>
    public sealed class InvalidArgumentException : Exception
    {
        /// <nodoc />
        public InvalidArgumentException(string message)
            : base(message)
        {
        }

        /// <nodoc />
        public InvalidArgumentException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
