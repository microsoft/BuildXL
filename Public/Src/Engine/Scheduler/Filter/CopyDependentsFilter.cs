// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Pips;

namespace BuildXL.Scheduler.Filter
{
    /// <summary>
    /// Dependents filter which only includes copy pips dependents
    /// </summary>
    public class CopyDependentsFilter : ClosureFunctionFilter<CopyDependentsFilter>
    {
        /// <summary>
        /// Class constructor
        /// </summary>
        public CopyDependentsFilter(PipFilter inner)
            : base(inner)
        {
        }

        /// <inheritdoc/>
        protected override IEnumerable<PipId> GetNeighborPips(IPipFilterContext context, PipId pip)
        {
            return context.GetDependents(pip).Where(p => context.GetPipType(p) == BuildXL.Pips.Operations.PipType.CopyFile);
        }

        /// <inheritdoc/>
        protected override CopyDependentsFilter CreateFilter(PipFilter inner)
        {
            return new CopyDependentsFilter(inner);
        }
    }
}
