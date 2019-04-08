// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class DownloadResolverSettings : ResolverSettings, IDownloadResolverSettings
    {
        /// <nodoc />
        public DownloadResolverSettings()
        {
            Downloads = new List<IDownloadFileSettings>();
        }

        /// <nodoc />
        public DownloadResolverSettings(IDownloadResolverSettings template, PathRemapper pathRemapper)
            : base(template, pathRemapper)
        {
            Contract.Assume(template != null);
            Contract.Assume(pathRemapper != null);

            Downloads = new List<IDownloadFileSettings>(template.Downloads.Count);
            foreach (var download in template.Downloads)
            {
                Downloads.Add(new DownloadFileSettings(download));
            }
        }
    
        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<IDownloadFileSettings> Downloads { get; set; }

        /// <inheritdoc />
        IReadOnlyList<IDownloadFileSettings> IDownloadResolverSettings.Downloads => Downloads;
    }
}
