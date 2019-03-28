// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.FrontEnd.Script.RuntimeModel
{
    /// <summary>
    /// Result of the parsing phase.
    /// </summary>
    public abstract class ParseResult<T>
    {
        internal ParseResult(T result)
        {
            Result = result;
            ErrorCount = 0;
            Diagnostics = CollectionUtilities.EmptyArray<Diagnostic>();
        }

        internal ParseResult(int errorCount)
        {
            ErrorCount = errorCount;
            Result = default(T);
            Diagnostics = CollectionUtilities.EmptyArray<Diagnostic>();
        }

        /// <summary>
        /// Returns true if parsing was successful
        /// </summary>
        public bool Success => ErrorCount == 0;

        /// <summary>
        /// Returns number of errors
        /// </summary>
        public int ErrorCount { get; }

        /// <summary>
        /// Returns result of successful parsing
        /// </summary>
        public T Result { get; private set; }

        /// <summary>
        /// Returns set of optional diagnostic objects.
        /// </summary>
        public IReadOnlyList<Diagnostic> Diagnostics { get; private set; }
    }
}
