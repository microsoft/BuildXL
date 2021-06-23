// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// A lazy expression to be evaluated with its expected return type
    /// </summary>
    public interface ILazyEval
    {
        /// <nodoc/>
        string Expression { get; }

        /// <nodoc/>
        string FormattedExpectedType { get; }
    }
}
