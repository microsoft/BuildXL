// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

#nullable enable

namespace BuildXL.Cache.Host.Configuration
{
    /// <nodoc />
    public class ContentCacheConfiguration
    {
        /// <nodoc />
        public TimeSpan DownloadTimeout { get; set; } = TimeSpan.FromMinutes(15);

        /// <nodoc />
        public int? DownloadConcurrency { get; set; } = Environment.ProcessorCount;
    }
}
