// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.VsPackage
{
    /// <summary>
    /// Exception class for BuildXLPackage
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors")]
    [SuppressMessage("Microsoft.Design", "CA1064:ExceptionsShouldBePublic")]
    [Serializable]
    internal sealed class BuildXLVsException : Exception
    {
        public BuildXLVsException()
        {
        }

        public BuildXLVsException(string message)
            : base(message)
        {
        }

        public BuildXLVsException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
