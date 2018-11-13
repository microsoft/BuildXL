// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Test.BuildXL.TestUtilities
{
    /// <summary>
    /// Exception thrown by tests to fail execution in a way that's not specific to a unit test framework.
    /// Only use if the caller cannot call an Assert method in the applicable test framework
    /// </summary>
    [SuppressMessage("Microsoft.Usage", "CA2237:MarkISerializableTypesWithSerializable")]
    [SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors")]
    public sealed class BuildXLTestException : Exception
    {
        /// <nodoc/>
        public BuildXLTestException(string message)
            : base(message) { }

        /// <nodoc/>
        public BuildXLTestException(string message, Exception inner)
            : base(message, inner) { }
    }
}
