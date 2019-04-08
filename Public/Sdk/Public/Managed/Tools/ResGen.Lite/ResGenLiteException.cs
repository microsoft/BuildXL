// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
