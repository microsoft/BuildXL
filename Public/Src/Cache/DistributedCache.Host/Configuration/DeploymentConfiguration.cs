// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;

#nullable disable

namespace BuildXL.Cache.Host.Configuration
{
    /// <summary>
    /// Describes drops/files which should be deployed
    /// </summary>
    public class DeploymentConfiguration
    {
        /// <summary>
        /// List of drops/files with target paths. Drops overlay with each other and later declarations can overwrite files from
        /// prior declarations if files overlap
        /// </summary>
        public List<DropDeploymentConfiguration> Drops { get; set; } = new List<DropDeploymentConfiguration>();

        /// <summary>
        /// Configuration for launching tool inside deployment
        /// </summary>
        public ServiceLaunchConfiguration Tool { get; set; }

        /// <summary>
        /// Configuration for proxies in a given stamp
        /// </summary>
        public GlobalProxyConfiguration Proxy { get; set; }

        /// <summary>
        /// The uri of key vault from which secrets should be retrieved
        /// </summary>
        public string KeyVaultUri { get; set; }

        /// <summary>
        /// Time to live for SAS urls returned by deployment service
        /// </summary>
        public TimeSpan SasUrlTimeToLive { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Indicates the Azure file share which should be used for generating SAS urls. If null,
        /// blob storage is used.
        /// NOTE: Content is assumed to already be present in file share
        /// </summary>
        public string AzureFileShareName { get; set; }

        /// <summary>
        /// The name of the secret used to communicate to storage account
        /// </summary>
        public SecretConfiguration AzureStorageSecretInfo { get; set; }

        /// <summary>
        /// The names of the allowed secrets used to authorize deployment queries
        /// </summary>
        public string[] AuthorizationSecretNames { get; set; } = new string[0];

        /// <summary>
        /// The time to live for cached authorization secrets in deployment service
        /// </summary>
        public TimeSpan AuthorizationSecretTimeToLive { get; set; } = TimeSpan.FromMinutes(30);
    }

    public class DropDeploymentConfiguration
    {
        /// <summary>
        /// The url used to download the drop
        /// This can point to a drop service drop url or source file/directory relative to source root:
        /// https://{accountName}.artifacts.visualstudio.com/DefaultCollection/_apis/drop/drops/{dropName}?root=release/win-x64
        /// 
        /// file://MyEnvironment/CacheConfiguration.json
        /// file://MyEnvironment/
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// The base url used to download the drop. This is only valid when combined with <see cref="RelativeRoot"/>
        /// </summary>
        public string BaseUrl { get; set; }

        /// <summary>
        /// The root directory under a provided drop to use. This is only valid when combined with <see cref="BaseUrl"/>
        /// </summary>
        public string RelativeRoot { get; set; }

        /// <summary>
        /// Gets the effective url used based on the values set for <see cref="Url"/>, <see cref="BaseUrl"/>, <see cref="RelativeRoot"/>.
        /// NOTE: <see cref="Url"/> takes precedence.
        /// </summary>
        public string EffectiveUrl => Url ?? (BaseUrl != null && RelativeRoot != null ? $"{BaseUrl}?root={RelativeRoot}" : null);

        /// <summary>
        /// Defines target folder under which deployment files should be placed.
        /// Optional. Defaults to root folder.
        /// </summary>
        public string TargetRelativePath { get; set; }

        /// <summary>
        /// Indicates that the drop is the primary drop used to determining the deployed drop name when logging
        /// </summary>
        public bool IsPrimary { get; set; }
    }

    /// <summary>
    /// Describes parameters used to launch tool inside a deployment
    /// </summary>
    public class ServiceLaunchConfiguration
    {
        /// <summary>
        /// The identifier used to identify the service for service lifetime management and interruption
        /// </summary>
        public string ServiceId { get; set; }

        /// <summary>
        /// Files watched by the service which are updated in place by the launcher
        /// </summary>
        public IReadOnlyList<string> WatchedFiles { get; set; } = new string[0];

        /// <summary>
        /// The time to wait for service to shutdown before terminating the process
        /// </summary>
        public double ShutdownTimeoutSeconds { get; set; }

        /// <summary>
        /// Path to the executable used when launching the tool relative to the layout root
        /// </summary>
        public string Executable { get; set; }

        /// <summary>
        /// Arguments used when launching the tool
        /// </summary>
        public string[] Arguments { get; set; } = new string[0];

        /// <summary>
        /// Environment variables used when launching the tool
        /// </summary>
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Environment variables for secrets used when launching the tool
        /// </summary>
        public Dictionary<string, SecretConfiguration> SecretEnvironmentVariables { get; set; } = new Dictionary<string, SecretConfiguration>();
    }

    /// <summary>
    /// Specifies secret consume by tool
    /// </summary>
    public class SecretConfiguration
    {
        /// <summary>
        /// Indicates the secret kind
        /// </summary>
        public SecretKind Kind { get; set; }

        /// <summary>
        /// The name of the secret for this environment variable
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The amount of time the secret can be cached before needing to be re-queried.
        /// </summary>
        public TimeSpan TimeToLive { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Overrides the key vault uri used to retrieve this secret
        /// </summary>
        public string OverrideKeyVaultUri { get; set; }
    }

    /// <summary>
    /// Defines configuration for proxy service process
    /// </summary>
    public class ProxyServiceConfiguration
    {
        /// <summary>
        /// The port used for proxy service on machine
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// The root path of the proxy's CAS
        /// </summary>
        public string RootPath { get; set; }

        /// <summary>
        /// The maximum number of parallel downloads of content
        /// </summary>
        public int? DownloadConcurrency { get; set; } = 30;

        /// <summary>
        /// The length of time proxy urls should live (this represents the time
        /// before the topology of the proxy network will be reshuffled)
        /// </summary>
        public TimeSpan ProxyAddressTimeToLive { get; set; }

        /// <summary>
        /// The size of retained content in download cache
        /// </summary>
        public int RetentionSizeGb { get; set; }

        /// <summary>
        /// The url of the service for retrieving proxy addresses
        /// </summary>
        public string DeploymentServiceUrl { get; set; }
    }

    /// <summary>
    /// Defines configuration for deployment proxy used to allow machines to act as proxies for the deployment service in a given stamp
    /// </summary>
    public class GlobalProxyConfiguration
    {
        /// <summary>
        /// Opaque string used to separate domains of machines which might form a proxy hierarchy
        /// </summary>
        public string Domain { get; set; } = string.Empty;

        /// <summary>
        /// The number of proxies in a stamp which should be seeded from storage
        /// </summary>
        public int Seeds { get; set; } = 3;

        /// <summary>
        /// The target number of machines
        /// </summary>
        public int FanOutFactor { get; set; } = 10;

        /// <summary>
        /// Overrides the normal proxy behavior and forces the machine to use the corresponding machine as its proxy
        /// </summary>
        public string OverrideProxyHost { get; set; }

        /// <summary>
        /// Gets whether the node is only capable of consuming from proxy nodes
        /// </summary>
        public bool ConsumerOnly { get; set; }

        /// <summary>
        /// Configuration for proxy service on machine
        /// </summary>
        public ProxyServiceConfiguration ServiceConfiguration { get; set; }

        /// <summary>
        /// Relative path to write <see cref="ServiceConfiguration"/> for consumption by proxy service executable
        /// </summary>
        public string TargetRelativePath { get; set; } = "ProxyConfiguration.json";
    }
}
