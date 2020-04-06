// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc/>
    public class ExtraArgumentsRushScript : IExtraArgumentsRushScript
    {
        /// <nodoc />
        public ExtraArgumentsRushScript()
        {}

        /// <nodoc />
        public ExtraArgumentsRushScript(IExtraArgumentsRushScript template)
        {
            Command = template.Command;
            ExtraArguments = template.ExtraArguments;
        }
        /// <inheritdoc/>
        public string Command { get; set; }

        /// <inheritdoc/>
        public DiscriminatingUnion<RushArgument, IReadOnlyList<RushArgument>> ExtraArguments { get; set; }
    }
}
