// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Pips;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Indicates the means by which a pip has ensured the presence of one of its outputs.
    /// </summary>
    public enum PipOutputOrigin : byte
    {
        /// <summary>
        /// The pip produced a new file, such as by writing new content or running a process.
        /// </summary>
        Produced,

        /// <summary>
        /// The pip didn't need to produce this file, since the correct content was already present.
        /// </summary>
        UpToDate,

        /// <summary>
        /// The correct content was not already present, but was deployed from a cache rather than produced anew.
        /// (this is a middle ground between <see cref="UpToDate" /> and <see cref="Produced" />)
        /// </summary>
        DeployedFromCache,

        /// <summary>
        /// The pip didn't materialize the output.
        /// </summary>
        NotMaterialized,
    }

    /// <summary>
    /// Extensions for <see cref="PipOutputOrigin"/>
    /// </summary>
    public static class PipOutputOriginExtensions
    {
        /// <summary>
        /// Converts a single-artifact origin to an overall pip result. This makes sense for pips with a single output.
        /// </summary>
        public static PipResultStatus ToPipResult(this PipOutputOrigin origin)
        {
            switch (origin)
            {
                case PipOutputOrigin.Produced:
                    return PipResultStatus.Succeeded;
                case PipOutputOrigin.UpToDate:
                    return PipResultStatus.UpToDate;
                case PipOutputOrigin.DeployedFromCache:
                    return PipResultStatus.DeployedFromCache;
                case PipOutputOrigin.NotMaterialized:
                    return PipResultStatus.NotMaterialized;
                default:
                    throw Contract.AssertFailure("Unhandled PipOutputOrigin");
            }
        }
    }
}
