// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Pips.Filter
{
    /// <summary>
    /// Dependents filter.
    /// </summary>
    public class DependentsFilter : ClosureFunctionFilter<DependentsFilter>
    {
        /// <summary>
        /// Class constructor
        /// </summary>
        public DependentsFilter(PipFilter inner)
            : base(inner)
        {
        }

        /// <inheritdoc/>
        protected override IEnumerable<PipId> GetNeighborPips(IPipFilterContext context, PipId pip)
        {
            return context.GetDependents(pip);
        }

        /// <inheritdoc/>
        protected override DependentsFilter CreateFilter(PipFilter inner)
        {
            return new DependentsFilter(inner);
        }
    }
}
