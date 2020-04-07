// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Rush.ProjectGraph
{
    /// <summary>
    /// A path (an output directory or a source file) that applies to a given collection of target scripts
    /// </summary>
    public sealed class PathWithTargets
    {
        /// <nodoc/>
        public PathWithTargets(AbsolutePath path, [CanBeNull] IReadOnlyList<string> targetScripts)
        {
            Path = path;
            TargetScripts = targetScripts?.ToReadOnlySet();
        }

        /// <nodoc/>
        public AbsolutePath Path { get; }

        /// <nodoc/>
        [CanBeNull]  
        public IReadOnlySet<string> TargetScripts { get; }

        /// <summary>
        /// Whether <see cref="Path"/> applies to the given script command name
        /// </summary>
        /// <remarks><see cref="TargetScripts"/> is an optional field, and when not present, the specified path applies to all scripts</remarks>
        public bool AppliesToScript(string scriptCommandName)
        {
            Contract.RequiresNotNullOrEmpty(scriptCommandName);

            return TargetScripts == null || TargetScripts.Contains(scriptCommandName);
        }
    }
}
