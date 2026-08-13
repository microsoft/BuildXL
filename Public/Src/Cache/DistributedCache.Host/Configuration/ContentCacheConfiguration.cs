// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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

        /// <summary>
        /// HTTPS origins that the content cache may download content from on a cache miss.
        /// </summary>
        public List<string> AllowedDownloadOrigins { get; set; } = new List<string>();
    }
}
