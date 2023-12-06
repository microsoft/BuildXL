// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Infrastructure a build is running on.
    /// </summary>
    public enum Infra : byte
    {
        /// <nodoc />
        Developer = 0,

        /// <nodoc />
        Ado = 1,

        /// <nodoc />
        CloudBuild = 2,
    }
}
