// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.InteropServices;

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

            CpuArchitecture = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => HostCpuArchitecture.X64,
                Architecture.X86 => HostCpuArchitecture.X86,
                Architecture.Arm64 => HostCpuArchitecture.Arm64,
                _ => throw new PlatformNotSupportedException($"Unsupported OS architecture '{RuntimeInformation.OSArchitecture}'."),
            };
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
