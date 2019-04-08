// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// The Host information of the machine that the current build is running on.
    /// </summary>
    public interface IHost
    {
        /// <summary>
        /// The Operating Systems of the hosts' cpu
        /// </summary>
        BuildXL.Interop.OperatingSystem CurrentOS { get; }

        /// <summary>
        /// The architecture of the hosts' cpu
        /// </summary>
        HostCpuArchitecture CpuArchitecture { get; }
    }

    /// <summary>
    /// The supported host os'
    /// </summary>
    public enum HostCpuArchitecture
    {
        /// <summary>
        /// Intel / Amd 32-bit
        /// </summary>
        X86 = 1,

        /// <summary>
        /// Intel / Amd 64-bit
        /// </summary>
        X64,
    }
}
