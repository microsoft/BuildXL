// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Diagnostics.ContractsLight;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// This class imposes a naming scheme on storage containers that are used for sharding. The reason for it is that we
/// want to separate content and metadata containers from each other, users from each other, and may have extra
/// criteria that's easier to express here.
/// </summary>
[JsonConverter(typeof(BlobCacheContainerNameJsonConverter))]
public record BlobCacheContainerName
{
    internal static readonly Regex LowercaseAlphanumericRegex = new(@"^[0-9a-z]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public BlobCacheVersion Version { get; }

    public BlobCacheContainerPurpose Purpose { get; }

    /// <summary>
    /// Identifies which cache is being used. The Universe is the unit of isolation.
    /// </summary>
    public string Universe { get; }

    /// <summary>
    /// Namespaces within a given universe can get cache hits from a namespace hierarchy, but can't get cache hits
    /// from each other. Because SAS tokens are set at the container level, they can be emitted so that a given
    /// build only has access to the specific namespaces that it needs access to.
    ///
    /// The cache tries to ensure that it is consistent across the namespace hierarchies to avoid causing build fragmentation.
    /// </summary>
    public string Namespace { get; }

    public string ContainerName { get; }

    public BlobCacheContainerName(
        BlobCacheVersion version,
        BlobCacheContainerPurpose purpose,
        string universe,
        string @namespace)
    {
        CheckValidUniverseAndNamespace(universe, @namespace);

        Version = version;
        Purpose = purpose;
        Universe = universe;
        Namespace = @namespace;

        ContainerName = CreateName();
    }

    public static void CheckValidUniverseAndNamespace(string universe, string @namespace)
    {
        if (!LowercaseAlphanumericRegex.IsMatch(universe))
        {
            throw new FormatException(
                $"{nameof(universe)} should be non-empty and composed of numbers and lower case letters. Universe=[{universe}]");
        }

        if (!LowercaseAlphanumericRegex.IsMatch(@namespace))
        {
            throw new FormatException(
                $"{nameof(@namespace)} should be non-empty and composed of numbers and lower case letters. Namespace=[{@namespace}]");
        }

        if (!(universe.Length + @namespace.Length <= 47))
        {
            throw new FormatException($"{nameof(universe)} and {nameof(@namespace)} must have less than 47 characters combined. Universe=[{universe}] Namespace=[{@namespace}]");
        }
    }

    /// <summary>
    /// Examples:
    ///  - contentv0u[universe]-[namespace]
    ///  - metadatav0u[universe]-[namespace]
    /// </summary>
    private static readonly Regex NameFormatRegex = new Regex(
        @"^(?<purpose>content|metadata)(?<version>v[0-9]+)u(?<universe>[a-z0-9]+)-(?<namespace>[a-z0-9]+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private string CreateName()
    {
        // Purpose has 10 characters reserved
        var purpose = Purpose.ToString().ToLowerInvariant();
        Contract.Assert(purpose.Length <= 10, $"Purpose ({nameof(BlobCacheContainerPurpose)}) exceeds the maximum length (10) allowed for {nameof(BlobCacheContainerName)}");

        // Version has 3 characters reserved
        var version = Version.ToString().ToLowerInvariant();
        Contract.Assert(version.Length is >= 1 and <= 3, $"Version ({nameof(BlobCacheVersion)}) exceeds the maximum length (3) allowed for {nameof(BlobCacheContainerName)}");

        // We have 16 characters reserved for the naming scheme. The maximum length is 63, and therefore 47 are
        // left for universe and namespace.
        var name = $"{purpose}{version}u{Universe}-{Namespace}";

        // See: https://learn.microsoft.com/en-us/rest/api/storageservices/naming-and-referencing-containers--blobs--and-metadata#container-names
        Contract.Assert(
            name.Length is >= 3 and <= 63,
            $"Generated blob name {name} which doesn't comply with Azure Blob Storage naming restrictions");

        return name;
    }

    public static BlobCacheContainerName Parse(string input)
    {
        var match = NameFormatRegex.Match(input);
        if (!match.Success)
        {
            throw new FormatException(message: $"Failed to match {nameof(NameFormatRegex)} to {input}");
        }

        var versionMatch = match.Groups["version"].Value;
        if (!Enum.TryParse<BlobCacheVersion>(versionMatch, ignoreCase: true, out var version))
        {
            throw new FormatException(message: $"Failed to parse version {versionMatch} into {nameof(BlobCacheVersion)}");
        }

        var purposeMatch = match.Groups["purpose"].Value;
        if (!Enum.TryParse<BlobCacheContainerPurpose>(purposeMatch, ignoreCase: true, out var purpose))
        {
            throw new FormatException(message: $"Failed to parse purpose {purposeMatch} into {nameof(BlobCacheContainerPurpose)}");
        }

        var universe = match.Groups["universe"].Value;
        var @namespace = match.Groups["namespace"].Value;

        return new BlobCacheContainerName(version, purpose, universe, @namespace);
    }

    public override string ToString()
    {
        return ContainerName;
    }
}
