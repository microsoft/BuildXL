// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Pips;
using BuildXL.Tracing;

namespace BuildXL.Scheduler.Filter
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
