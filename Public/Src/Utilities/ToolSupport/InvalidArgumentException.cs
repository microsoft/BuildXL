// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
