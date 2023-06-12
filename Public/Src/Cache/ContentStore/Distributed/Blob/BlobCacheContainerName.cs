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

    public const int StorageContainerNameMaximumLength = 63;
    public const int VersionReservedLength = 3;
    public const int PurposeReservedLength = 10;
    public const int MatrixReservedLength = 10;
    public const int DelimitersReservedLength = 3;
    public const int UniverseNamespaceReservedLength = StorageContainerNameMaximumLength - (VersionReservedLength + PurposeReservedLength + MatrixReservedLength + DelimitersReservedLength);

    /// <summary>
    /// Current version of the cache. See: <see cref="BlobCacheVersion"/>.
    /// </summary>
    public BlobCacheVersion Version { get; }

    /// <summary>
    /// Purpose of the container. See: <see cref="BlobCacheContainerPurpose"/>.
    /// </summary>
    public BlobCacheContainerPurpose Purpose { get; }

    /// <summary>
    /// The matrix is an internal parameter used to enforce a cache miss in specific circumstances, such as when
    /// sharding scheme changes.
    /// </summary>
    public string Matrix { get; }

    /// <summary>
    /// Identifies which cache is being used. The Universe is the unit of isolation.
    /// </summary>
    /// <remarks>
    /// Universe is customer-controlled.
    /// </remarks>
    public string Universe { get; }

    /// <summary>
    /// Namespaces within a given universe can get cache hits from a namespace hierarchy, but can't get cache hits
    /// from each other. Because SAS tokens are set at the container level, they can be emitted so that a given
    /// build only has access to the specific namespaces that it needs access to.
    ///
    /// The cache tries to ensure that it is consistent across the namespace hierarchies to avoid causing build fragmentation.
    /// </summary>
    /// <remarks>
    /// Namespace is customer-controlled.
    /// </remarks>
    public string Namespace { get; }

    /// <summary>
    /// The full container name. This is equivalent to calling <see cref="ToString"/>.
    /// </summary>
    public string ContainerName { get; }

    public BlobCacheContainerName(
        BlobCacheVersion version,
        BlobCacheContainerPurpose purpose,
        string matrix,
        string universe,
        string @namespace)
    {
        if (!LowercaseAlphanumericRegex.IsMatch(matrix))
        {
            throw new FormatException(
                $"{nameof(matrix)} should be non-empty and composed of numbers and lower case letters. Matrix=[{matrix}]");
        }

        CheckValidUniverseAndNamespace(universe, @namespace);

        Version = version;
        Purpose = purpose;
        Matrix = matrix;
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

        if (!(universe.Length + @namespace.Length <= UniverseNamespaceReservedLength))
        {
            throw new FormatException($"{nameof(universe)} and {nameof(@namespace)} must have less than {UniverseNamespaceReservedLength} characters combined. Universe=[{universe}] Namespace=[{@namespace}]");
        }
    }

    /// <summary>
    /// Examples:
    ///  - contentv0u[universe]-[namespace]
    ///  - metadatav0u[universe]-[namespace]
    /// </summary>
    private static readonly Regex NameFormatRegex = new Regex(
        @"^(?<purpose>content|metadata)(?<version>v[0-9]+)-(?<matrix>[a-z0-9]+)-(?<universe>[a-z0-9]+)-(?<namespace>[a-z0-9]+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private string CreateName()
    {
        // Purpose has 10 characters reserved
        var purpose = Purpose.ToString().ToLowerInvariant();
        Contract.Assert(purpose.Length <= PurposeReservedLength, $"Purpose ({nameof(BlobCacheContainerPurpose)}) exceeds the maximum length ({PurposeReservedLength}) allowed for {nameof(BlobCacheContainerName)}");

        // Version has 3 characters reserved
        var version = Version.ToString().ToLowerInvariant();
        Contract.Assert(version.Length is >= 1 and <= VersionReservedLength, $"Version ({nameof(BlobCacheVersion)}) exceeds the maximum length ({VersionReservedLength}) allowed for {nameof(BlobCacheContainerName)}");

        var matrix = Matrix.ToLowerInvariant();
        Contract.Assert(matrix.Length <= MatrixReservedLength, $"Matrix exceeds the maximum length ({MatrixReservedLength}) allowed for {nameof(BlobCacheContainerName)}");

        var name = $"{purpose}{version}-{Matrix}-{Universe}-{Namespace}";

        // See: https://learn.microsoft.com/en-us/rest/api/storageservices/naming-and-referencing-containers--blobs--and-metadata#container-names
        Contract.Assert(
            name.Length is >= 3 and <= StorageContainerNameMaximumLength,
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

        var matrix = match.Groups["matrix"].Value;
        var universe = match.Groups["universe"].Value;
        var @namespace = match.Groups["namespace"].Value;

        return new BlobCacheContainerName(version, purpose, matrix, universe, @namespace);
    }

    public override int GetHashCode()
    {
        return ContainerName.GetHashCode();
    }

    public override string ToString()
    {
        return ContainerName;
    }
}
