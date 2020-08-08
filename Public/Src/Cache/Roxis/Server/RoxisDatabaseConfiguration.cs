// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.Roxis.Server
{
    /// <summary>
    /// Configuration for <see cref="RoxisDatabase"/>
    /// </summary>
    public class RoxisDatabaseConfiguration
    {
        public string Path { get; set; } = string.Empty;

        public bool EnableWriteAheadLog { get; set; } = true;

        public bool EnableFSync { get; set; } = true;
    }
}
