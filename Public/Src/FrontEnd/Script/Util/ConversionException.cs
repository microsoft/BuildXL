// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.FrontEnd.Script.Evaluator;

namespace BuildXL.FrontEnd.Script.Util
{
    /// <summary>
    /// Conversion exception
    /// </summary>
    public sealed class ConversionException : Exception
    {
        /// <summary>
        /// Conversion context.
        /// </summary>
        public readonly ErrorContext ErrorContext;

        /// <inheritDoc/>
        public ConversionException(string message, ErrorContext errorContext)
            : base(message)
        {
            ErrorContext = errorContext;
        }
    }
}
