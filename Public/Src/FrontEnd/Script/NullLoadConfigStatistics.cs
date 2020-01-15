// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces;

namespace BuildXL.FrontEnd.Script
{
    /// <nodoc />
    internal class NullLoadConfigStatistics : ILoadConfigStatistics
    {
        /// <inheritdoc />
        public Counter FileCountCounter => new Counter();

        /// <inheritdoc />
        public int FileCount => 0;

        /// <inheritdoc />
        public Counter TotalDuration => new Counter();

        /// <inheritdoc />
        public int TotalDurationMs => 0;

        /// <inheritdoc />
        public Counter ParseDuration => new Counter();

        /// <inheritdoc />
        public int ParseDurationMs => 0;

        /// <inheritdoc />
        public Counter ConversionDuration => new Counter();

        /// <inheritdoc />
        public int ConversionDurationMs => 0;
    }
}
