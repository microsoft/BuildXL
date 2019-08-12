// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    using static ExecutionEnvironmentParts;

    /// <summary>
    /// Execution environments for telemetry.
    /// </summary>
    /// <remarks>
    /// We send it via an ETW event as well so that's why it is not byte. Byte type causes some issues as an ETW event argument.
    /// Values should be constructed using components Group | Stage | Location from <see cref="ExecutionEnvironmentParts"/>
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
        SelfHostLKG = SelfHost | NoStage | Lab,

        /// <summary>
        /// BuildXL self host built locally
        /// </summary>
        SelfHostPrivateBuild = SelfHost | NoStage | Dev,

        /// <summary>
        /// OSG build lab
        /// </summary>
        OsgLab = Osg | NoStage | Lab,

        /// <summary>
        /// OSG Prime lab build
        /// </summary>
        OsgPrimeLab = Osg | NoStage | PrimeLab,

        /// <summary>
        /// OSG dev machine build
        /// </summary>
        OsgDevMachine = Osg | NoStage | Dev,

        /// <summary>
        /// Nightly performance testing
        /// </summary>
        NightlyPerformanceRun = SelfHost | NightlyPerf | NoLocation,

        /// <summary>
        /// BuildXL invocations from OSG's WrapItUp tool
        /// </summary>
        OsgWrapItUp = Osg | WrapItUp | NoLocation,

        /// <summary>
        /// The OsgTools repo
        /// </summary>
        OsgTools = Osg | Tools | NoLocation,

        ///////////////// Office environment //////////////////////////

        /// <summary>
        /// Office enlistment-build on developer machine.
        /// </summary>
        OfficeEnlistmentBuildDev = Office | EnlistBuild | Dev,

        /// <summary>
        /// Office enlistment-build on build lab.
        /// </summary>
        OfficeEnlistmentBuildLab = Office | EnlistBuild | Lab,

        /// <summary>
        /// Office meta-build on developer machine.
        /// </summary>
        OfficeMetaBuildDev = Office | MetaBuild | Dev,

        /// <summary>
        /// Office meta-build on build lab.
        /// </summary>
        OfficeMetaBuildLab = Office | MetaBuild | Lab,

        /// <summary>
        /// Office product-build on developer machine.
        /// </summary>
        OfficeProductBuildDev = Office | ProductBuild | Dev,

        /// <summary>
        /// Office product-build (or regular-build) on build lab.
        /// </summary>
        OfficeProductBuildLab = Office | ProductBuild | Lab,

        //////////////// Visual C++ environment ///////////////////////

        /// <summary>
        /// Visual cpp team tests as run in the lab
        /// </summary>
        VisualCppTestsLab = VisualCpp | Tests | Lab,

        /// <summary>
        /// Visual cpp team tests as run by devs
        /// </summary>
        VisualCppTestsDev = VisualCpp | Tests | Dev,

        ///////////////// Office macOS //////////////////////////

        /// <summary>
        /// Office APEX build in the build lab
        /// </summary>
        OfficeAPEXLab = OfficeApex | NoStage | Lab,

        /// <summary>
        /// Office APEX build on a dev machine
        /// </summary>
        OfficeAPEXDev = OfficeApex | NoStage | Dev,

        ///////////////////////////////////////////////////////////////
    }

    /// <summary>
    /// Defines components of <see cref="ExecutionEnvironment"/> enum values.
    /// Values should be mutually exclusive for their bit range and not set any bits in other bit ranges.
    /// 
    /// 1st and 2nd hex chars (8 bits) is product group
    /// 3rd hex char (4 bits) is build stage
    /// 4th hex char (4 bits) is build location
    /// </summary>
    internal enum ExecutionEnvironmentParts
    {
        // Group 0xXX__
        SelfHost   = 0xBD00,
        Office     = 0x0F00,
        OfficeApex = 0x0A00,
        OsgTools   = 0x0200,
        Osg        = 0x0500,
        VisualCpp  = 0x7C00,

        // Stage 0x__X0
        NoStage      = 0x0000,
        EnlistBuild  = 0x0010,
        MetaBuild    = 0x0020,
        ProductBuild = 0x0030,
        Tests        = 0x0040,
        WrapItUp     = 0x0050,
        Tools        = 0x0060,
        NightlyPerf  = 0x0070,

        // Location 0x___X
        NoLocation = 0x0000,
        Lab        = 0x0007,
        PrimeLab   = 0x0009,
        Dev        = 0x000D,
    }
}
