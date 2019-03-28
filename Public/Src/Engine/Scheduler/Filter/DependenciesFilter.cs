// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Pips;

namespace BuildXL.Scheduler.Filter
{
    /// <summary>
    /// Dependencies filter.
    /// </summary>
    public class DependenciesFilter : ClosureFunctionFilter<DependenciesFilter>
    {
        /// <summary>
        /// Class constructor
        /// </summary>
        public DependenciesFilter(PipFilter inner, ClosureMode closureMode = ClosureMode.TransitiveIncludingSelf)
            : base(inner, closureMode)
        {
        }

        /// <inheritdoc/>
        protected override IEnumerable<PipId> GetNeighborPips(IPipFilterContext context, PipId pip)
        {
            return context.GetDependencies(pip);
        }

        /// <inheritdoc/>
        protected override DependenciesFilter CreateFilter(PipFilter inner)
        {
            return new DependenciesFilter(inner, ClosureMode);
        }
    }
}
