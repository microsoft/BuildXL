// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class Host : IHost
    {
        /// <summary>
        /// Gets the current host information
        /// </summary>
        public static IHost Current { get; } = new Host();

        /// <nodoc />
        public Host()
        {
            CurrentOS = BuildXL.Interop.Dispatch.CurrentOS();

            // $Future we don't handle Arm or other Cpu's yet
            CpuArchitecture = Environment.Is64BitOperatingSystem ? HostCpuArchitecture.X64 : HostCpuArchitecture.X86;
        }

        /// <nodoc />
        public Host(IHost template, PathRemapper pathRemapper)
        {
            Contract.Assume(template != null);
            Contract.Assume(pathRemapper != null);

            CurrentOS = template.CurrentOS;
            CpuArchitecture = template.CpuArchitecture;
        }

        /// <inheritdoc />
        public BuildXL.Interop.OperatingSystem CurrentOS { get; set;  }

        /// <inheritdoc />
        public HostCpuArchitecture CpuArchitecture { get; set; }
    }
}
