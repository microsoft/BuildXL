// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Pips.Filter
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
