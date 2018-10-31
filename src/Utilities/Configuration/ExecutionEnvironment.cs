// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Execution environments for telemetry.
    /// </summary>
    /// <remarks>
    /// We send it via an ETW event as well so that's why it is not byte. Byte type causes some issues as an ETW event argument.
    /// </remarks>
    public enum ExecutionEnvironment
    {
        /// <summary>
        /// The environment is unknown
        /// </summary>
        Unset = 0,

        /// <summary>
        /// BuildXL self host LKG nuget package
        /// </summary>
        SelfHostLKG,

        /// <summary>
        /// BuildXL self host built locally
        /// </summary>
        SelfHostPrivateBuild,

        /// <summary>
        /// OSG build lab
        /// </summary>
        OsgLab,

        /// <summary>
        /// OSG Prime lab build
        /// </summary>
        OsgPrimeLab,

        /// <summary>
        /// OSG dev machine build
        /// </summary>
        OsgDevMachine,

        /// <summary>
        /// Nightly performance testing
        /// </summary>
        NightlyPerformanceRun,

        /// <summary>
        /// Mimic build of Windows source tree
        /// </summary>
        MimicWindows,

        /// <summary>
        /// BuildXL invocations from OSG's WrapItUp tool
        /// </summary>
        OsgWrapItUp,

        /// <summary>
        /// The OsgTools repo
        /// </summary>
        OsgTools,

        ///////////////// Office environment //////////////////////////

        /// <summary>
        /// Office enlistment-build on developer machine.
        /// </summary>
        OfficeEnlistmentBuildDev,

        /// <summary>
        /// Office enlistment-build on build lab.
        /// </summary>
        OfficeEnlistmentBuildLab,

        /// <summary>
        /// Office meta-build on developer machine.
        /// </summary>
        OfficeMetaBuildDev,

        /// <summary>
        /// Office meta-build on build lab.
        /// </summary>
        OfficeMetaBuildLab,

        /// <summary>
        /// Office product-build on developer machine.
        /// </summary>
        OfficeProductBuildDev,

        /// <summary>
        /// Office product-build (or regular-build) on build lab.
        /// </summary>
        OfficeProductBuildLab,

        //////////////// Visual C++ environment ///////////////////////

        /// <summary>
        /// Visual cpp team tests as run in the lab
        /// </summary>
        VisualCppTestsLab,

        /// <summary>
        /// Visual cpp team tests as run by devs
        /// </summary>
        VisualCppTestsDev,

        ///////////////// Office macOS //////////////////////////

        /// <summary>
        /// Office APEX build in the build lab
        /// </summary>
        OfficeAPEXLab,

        /// <summary>
        /// Office APEX build on a dev machine
        /// </summary>
        OfficeAPEXDev,

        ///////////////////////////////////////////////////////////////
    }
}
