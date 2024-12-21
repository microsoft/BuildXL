// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc/>
    public class CustomSchedulingCallback : ICustomSchedulingCallback
    {
        /// <nodoc/>
        public CustomSchedulingCallback()
        {
        }

        /// <nodoc/>
        public CustomSchedulingCallback(ICustomSchedulingCallback template)
        {
            Module = template.Module;
            SchedulingFunction = template.SchedulingFunction;
            Argument = template.Argument;
        }

        /// <inheritdoc/>
        public string Module { get; set; }

        /// <inheritdoc/>
        public string SchedulingFunction { get; set; }

        /// <inheritdoc/>
        public ILazyEval Argument { get; set; }
    }
}
