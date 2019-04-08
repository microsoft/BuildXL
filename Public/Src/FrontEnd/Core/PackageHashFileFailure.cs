// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Failure details for IO operations with a package hash file.
    /// </summary>
    public sealed class PackageHashFileFailure : Failure
    {
        /// <nodoc />
        public string Error { get; }

        /// <nodoc />
        public PackageHashFileFailure(string error)
        {
            Error = error;
        }
        
        /// <inheritdoc />
        [SuppressMessage("Microsoft.Globalization", "CA1305:SpecifyIFormatProvider")]
        public override string Describe()
        {
            return Error;
        }

        /// <inheritdoc />
        public override BuildXLException CreateException()
        {
            return new BuildXLException(Describe());
        }

        /// <inheritdoc />
        public override BuildXLException Throw()
        {
            throw CreateException();
        }
    }
}
