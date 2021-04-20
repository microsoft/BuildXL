// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Stores;
using CLAP;

// ReSharper disable UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        private const string MaxSizeDescription = "<hard-limit-expression>[:<soft-limit-expression>]";
        private const string DiskFreePercentDescription = MaxSizeDescription;

        /// <summary>
        ///     Verb to set content quota.
        /// </summary>
        [Verb(Description = "Show or set quota for a CAS")]
        internal void Quota
            (
            [Required, Description("CAS root directory")] string root,
            [Description(MaxSizeDescription)] string maxSize,
            [Description(DiskFreePercentDescription)] string diskFreePercent,
            [DefaultValue(false), Description("Bring cache immediately into new quota")] bool purge,
            [DefaultValue(false), Description("Display json format")] bool json
            )
        {
            Initialize();
            var rootPath = new AbsolutePath(root);

            if (string.IsNullOrEmpty(maxSize) && string.IsNullOrEmpty(diskFreePercent))
            {
                Result<ContentStoreConfiguration> result = _fileSystem.ReadContentStoreConfigurationAsync(rootPath).Result;
                if (result.Succeeded)
                {
                    ShowConfiguration(result.Value, json);
                }
                else
                {
                    _logger.Error("Failed to read configuration, result=[{result}]");
                    return;
                }
            }
            else
            {
                var configuration = new ContentStoreConfiguration(maxSize, diskFreePercent);
                configuration.Write(_fileSystem, rootPath).Wait();
                ShowConfiguration(configuration, json);
            }

            if (purge)
            {
                RunFileSystemContentStoreInternal(rootPath, async (context, store) =>
                {
                    await store.SyncAsync(context).ConfigureAwait(false);
                });
            }
        }

        private void ShowConfiguration(ContentStoreConfiguration configuration, bool json)
        {
            var message = $"{(json ? configuration.SerializeToJSON() : configuration.ToString())}";
            _logger.Log(Severity.Always, message);
        }
    }
}
