// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc/>
    public class LazyEval : ILazyEval
    {
        /// <nodoc/>
        public LazyEval()
        { }

        /// <nodoc/>
        public LazyEval(ILazyEval template)
        {
            Expression = template.Expression;
            FormattedExpectedType = template.FormattedExpectedType;
        }

        /// <inheritdoc/>
        public string Expression { get; set; }

        /// <inheritdoc/>
        public string FormattedExpectedType { get; set; }
    }
}
