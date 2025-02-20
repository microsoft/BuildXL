# Configuring a shared cache backed by an Azure Blob Storage Account

An Azure Storage Blob account can be used to host a shared cache for BuildXL builds. Any running BuildXL instance that has access to the blob storage account (and has the proper credentials) can store and retrieve information from the shared cache. If you are interested in knowing more details about the cache and its multi-tier architecture, check [here](../../Public/Src/Cache/Readme.md).

**Caution!** There are security implications to utilizing a shared cache that lives across build invocations. Be careful to control access to the backing cache and be intentional about which builds are trusted to publish data into the cache.

In this section we will explain how to create an Azure Storage Blob account that can host a BuildXL cache and how to configure BuildXL to use it.

## Creating a storage account
A cache provisioning script is available. The script creates a blob storage account and set proper lifetime management policies to evict content that is too old and prevent the storage account to grow unbounded. In order to create a shared cache, make sure you have installed the [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli), open a PowerShell console and execute:

```
az login
iex "& { $(irm  https://bxlscripts.blob.core.windows.net/provisioning/provision-l3cache.ps1) } -resourceGroup <group> -subscription <guid> -azureRegion <region> -blobAccountName <name>"
```
Here `az login` is just one possible way to log into Azure. The login session needs to have sufficient permissions to create a blob account under the specified subscription.

## Configuring BuildXL to use a Blob-based shared cache

A cache configuration file should be provided to BuildXL so a blob-based cache is used. Here is an example that sets a local cache together with a remote blob-based cache:

```json
{
  "Assembly": "BuildXL.Cache.MemoizationStoreAdapter",
  "Type": "BuildXL.Cache.MemoizationStoreAdapter.BlobWithLocalCacheFactory",
  "RemoteCache": {
    "CacheId": "remoteexamplecache",
    "CacheLogPath": "[BuildXLSelectedLogPath].log",
    "Universe": "default",
    "RetentionPolicyInDays": 6
  },
  "LocalCache": {
    "CacheId": "localexamplecache",
    "MaxCacheSizeInMB": 40480,
    "CacheLogPath": "[BuildXLSelectedLogPath].local.log",
    "CacheRootPath": "[BuildXLSelectedRootPath]",
  }
}
```

The relevant fields for the `RemoteCache` sections are:
* The `CacheId`, used for logging/error reporting to identify the cache in question.
* The `Universe`, which defines the cache universe: builds sharing the same cache universe can actually interchange information.
* The `RetentionPolicyInDays`. This is the retention policy configured in the section above. The above provisioning script defines 6 days of retention. It is important to keep this value in sync with the management policies of the blob account, if they were to be changed. A blob retention policy lower than the value specified here can cause build failures.

The cache configuration file can then be passed to BuildXL via command line arguments:

`bxl.exe /cacheConfigFilePath:<path to the cache config file>`

 ## Authenticating

The proper credentials need to be provided in order for the BuildXL cache to store and retrieve content from the blob storage account. There are a number of supported auth mechanisms:

### Using a managed identity
 Create a [user-assigned](https://learn.microsoft.com/en-us/azure/active-directory/managed-identities-azure-resources/how-manage-user-assigned-managed-identities) managed identity and configure it to have `Storage Blob Data Contributor` permissions to access the blob account. The context running the build then needs to be able to provide the created identity when authenticating against Azure. In order to instruct the cache to use managed identities, the identity to be used and the storage account endpoint need to be specified in the cache config:


 ```json
 "RemoteCache": {
    "CacheLogPath": "[BuildXLSelectedLogPath].log",
    "CacheId": "remoteexamplecache",
    "Universe": "exampleuniverse",
    "RetentionPolicyInDays": 6,
    "StorageAccountEndpoint": "https://exampleblobstorage.blob.core.windows.net",
    "ManagedIdentityId": "012345678-01234-01234-01234-0123456789012"
 }
 ```

### Using codespaces authentication
Under github [codespaces](https://github.com/features/codespaces), a VSCode extension [Azure Devops Codespaces Authentication](https://github.com/microsoft/ado-codespaces-auth/) can be used for providing seamless authentication against Azure DevOps using Entra ID login. BuildXL will look for the `azure-auth-helper` tool under `PATH`, and interact with this auth helper in order to get a valid Entra ID token to use to access the blob account. The authenticated user (or containing security group) needs to have `Storage Blob Data Contributor` permissions to access the blob account. BuildXL will only use this auth method when the aforementioned tool is found in `PATH` and the `StorageAccountEndpoint` is provided.

### Using interactive browser authentication
A user-interactive authentication mechanism via a web browser can be used to acquire an Entra ID token. This auth mechanism will only be attempted if the `StorageAccountEndpoint` is provided and BuildXL is run with the `/interactive` flag, indicating that this is a developer build and therefore interactive prompts are allowed. The interactive prompt will try to acquire a token via Entra ID authentication. Similarly to the above auth method, the blob storage account needs to have configured access such that the authenticated user (or containing security group) has `Storage Blob Data Contributor` permissions.

## Developer cache
A developer cache is a configuration where local builds can benefit from cache hits coming from lab builds. The recommended configuration is such that:
* Developer builds only 'pull' from the cache, but cannot write to it. This is in order to avoid security issues, where dev boxes are typically a less controlled environment than a lab build. Pushing bad content into the cache can have a ripple effect if a malicious actor takes control of a developer box.
* The cache is regularly warmed up with a lab build. This can happen as part of running a regular baseline or CI build, or by having a dedicated pipeline that seeds the dev cache. This is usually called a 'publishing' build, and it naturally needs to have both read and write permissions to the cache.

In order to configure a publishing build, the steps described above should be followed. The only relevant consideration is that in order to successfully get cross cache hits (from labs to dev boxes) pip fingerprints needs to match. These imply a uniform disk layout and matching environment variables, where used. You can use the [cache miss analysis](Documentation/Wiki/Advanced-Features/Cache-Miss-Analysis.md) tool in order to understand misses.

On the developer side, the cache configuration file needs to specify the same `StorageAccountEndpoint` the publishing build will be using for seeding the content. The cache config should also set the remote cache to be 'read-only' so no pushes are attempted:

```json
 "RemoteCache": {
    "CacheLogPath": "[BuildXLSelectedLogPath].Remote.log",
    "StorageAccountEndpoint": "https://exampleblobstorage.blob.core.windows.net",
    "IsReadOnly": true
 }
 ```
 
In order to do authentication, either [Codespaces](#using-codespaces-authentication) or [interactive browser](#using-interactive-browser-authentication) authentication are the recommended auth mechanisms. Both will try to acquire an Entra ID token. Make sure the destination blob storage account only has `Storage Blob Data Reader` permissions for that user/security group, in order to prevent any undesired writes.