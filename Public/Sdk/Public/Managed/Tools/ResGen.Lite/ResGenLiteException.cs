// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace ResGen.Lite
{
    /// <summary>
    /// Exception for user level errors
    /// </summary>
    public class ResGenLiteException : Exception
    {
        /// <nodoc />
        public ResGenLiteException(string message) : base(message)
        {
        }

        /// <nodoc />
        public ResGenLiteException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
