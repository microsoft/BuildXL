// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
