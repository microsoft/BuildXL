// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Pips;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Extensions for <see cref="ContentMaterializationOrigin"/>.
    /// </summary>
    public static class ContentMaterializationOriginExtensions
    {
        /// <nodoc />
        public static PipOutputOrigin ToPipOutputOrigin(this ContentMaterializationOrigin origin)
        {
            switch (origin)
            {
                case ContentMaterializationOrigin.DeployedFromCache:
                    return PipOutputOrigin.DeployedFromCache;
                case ContentMaterializationOrigin.UpToDate:
                    return PipOutputOrigin.UpToDate;
                default:
                    throw Contract.AssertFailure("Unhandled ContentMaterializationOrigin");
            }
        }

        /// <summary>
        /// This is like <see cref="ToPipOutputOrigin"/>, but treating <see cref="ContentMaterializationOrigin.DeployedFromCache"/>
        /// as <see cref="BuildXL.Scheduler.PipOutputOrigin.Produced" /> since historically WriteFile and CopyFile outputs would be
        /// 'produced' if not already up to date at the target; now that we always keep their outputs in cache, we could instead
        /// say 'deployed from cache' and never 'produced', but we choose to reserve that status for cached process outputs.
        /// </summary>
        public static PipOutputOrigin ToPipOutputOriginHidingDeploymentFromCache(this ContentMaterializationOrigin origin)
        {
            switch (origin)
            {
                case ContentMaterializationOrigin.DeployedFromCache:
                    return PipOutputOrigin.Produced;
                case ContentMaterializationOrigin.UpToDate:
                    return PipOutputOrigin.UpToDate;
                default:
                    throw Contract.AssertFailure("Unhandled ContentMaterializationOrigin");
            }
        }

        /// <summary>
        /// Inverse of <see cref="ToPipOutputOriginHidingDeploymentFromCache(ContentMaterializationOrigin)"/> for <see cref="PipResultStatus"/>
        /// </summary>
        public static ContentMaterializationOrigin ToContentMaterializationOriginHidingExecution(this PipResultStatus status)
        {
            switch (status)
            {
                case PipResultStatus.DeployedFromCache:
                case PipResultStatus.Succeeded:
                    return ContentMaterializationOrigin.DeployedFromCache;
                case PipResultStatus.UpToDate:
                    return ContentMaterializationOrigin.UpToDate;
                default:
                    throw Contract.AssertFailure("Unhandled ContentMaterializationOrigin");
            }
        }
    }
}
