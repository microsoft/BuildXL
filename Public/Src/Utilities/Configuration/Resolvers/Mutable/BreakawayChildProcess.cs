// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc />
    public sealed class BreakawayChildProcess : IBreakawayChildProcess
    {
        /// <nodoc />
        public BreakawayChildProcess()
        {
            RequiredArguments = string.Empty;
        }

        /// <nodoc />
        public BreakawayChildProcess(IBreakawayChildProcess template, PathRemapper pathRemapper)
        {
            ProcessName = pathRemapper.Remap(template.ProcessName);
            RequiredArguments = template.RequiredArguments;
            RequiredArgumentsIgnoreCase = template.RequiredArgumentsIgnoreCase;
        }

        /// <inheritdoc/>
        public PathAtom ProcessName { get; set; }

        /// <inheritdoc/>
        public string RequiredArguments { get; set; }

        /// <inheritdoc/>
        public bool RequiredArgumentsIgnoreCase { get; set; }
    }
}
