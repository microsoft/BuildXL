// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
