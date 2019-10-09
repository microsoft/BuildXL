// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// How a pip is related to service pips.
    /// </summary>
    public enum ServicePipKind : byte
    {
        /// <summary>Has nothing to do with service pips whatsoever.</summary>
        None = 0,

        /// <summary>Service/daemon process.</summary>
        Service = 1,

        /// <summary>A process that talks to a service pip.  Must have non-empty <see cref="ServiceInfo.ServicePipDependencies" /> </summary>
        ServiceClient = 2,

        /// <summary>Service shutdown process, which BuildXL is to use to gracefully shut down the service.</summary>
        ServiceShutdown = 3,

        /// <summary>Service finalization process, which BuildXL is to schedule after all service client pips.</summary>
        ServiceFinalization = 4,
    }

    /// <summary>
    /// Extension methods for <see cref="ServicePipKind"/>.
    /// </summary>
    public static class ServicePipKindMethods
    {
        /// <summary>
        /// Returns true if <paramref name="kind"/> is either <see cref="ServicePipKind.Service"/>
        /// or <see cref="ServicePipKind.ServiceShutdown"/>.
        /// </summary>
        public static bool IsStartOrShutdown(this ServicePipKind kind)
        {
            return kind == ServicePipKind.Service || kind == ServicePipKind.ServiceShutdown;
        }
    }

    /// <summary>
    /// Utilities for extracting <see cref="ServicePipKind"/> from a pip.
    /// </summary>
    public static class ServicePipKindUtil
    {
        /// <summary>
        /// Checks if a pip is a service start, shutdown, or finalization.
        /// </summary>
        public static bool IsServiceStartShutdownOrFinalizationPip(Pip pip)
        {
            var serviceKind = ServiceKind(pip);
            return
                serviceKind == ServicePipKind.Service ||
                serviceKind == ServicePipKind.ServiceShutdown ||
                serviceKind == ServicePipKind.ServiceFinalization;
        }

        /// <summary>
        /// Gets the <see cref="ServicePipKind"/> of a pip.
        /// </summary>
        public static ServicePipKind ServiceKind(Pip pip)
        {
            if (pip.PipType == PipType.Process)
            {
                return ServiceKind((Process)pip);
            }

            if (pip.PipType == PipType.Ipc)
            {
                return ((IpcPip)pip).IsServiceFinalization
                    ? ServicePipKind.ServiceFinalization
                    : ServicePipKind.ServiceClient;
            }

            return ServicePipKind.None;
        }

        private static ServicePipKind ServiceKind(Process pip)
        {
            return pip.ServiceInfo?.Kind ?? ServicePipKind.None;
        }

        /// <summary>
        /// Checks if a pip is a service related pip.
        /// </summary>
        /// <param name="pip"></param>
        /// <returns></returns>
        public static bool IsServiceRelatedPip(Pip pip) => ServiceKind(pip) != ServicePipKind.None;
    }
}
