// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc/>
    public class RushCommand : IRushCommand
    {
        /// <nodoc />
        public RushCommand()
        {
            Command = string.Empty;
            DependsOn = new List<IRushCommandDependency>();
        }

        /// <nodoc />
        public RushCommand(IRushCommand template)
        {
            Command = template.Command;
            DependsOn = template.DependsOn ?? new List<IRushCommandDependency>();
        }

        /// <inheritdoc/>
        public string Command { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<IRushCommandDependency> DependsOn { get; set; }
    }
}
