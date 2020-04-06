// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    ///  Appends arguments to an existing script defined in package.json
    /// </summary>
    public interface IExtraArgumentsRushScript
    {
        /// <nodoc/>
        string Command { get; }

        /// <nodoc/>
        DiscriminatingUnion<RushArgument, IReadOnlyList<RushArgument>> ExtraArguments { get; }
    }
}
