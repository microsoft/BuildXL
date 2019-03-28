// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Represents a unique identifier of a nuget package.
    /// </summary>
    public readonly struct PackageIdentity : IEquatable<PackageIdentity>
    {
        /// <nodoc />
        public string Protocol { get; }

        /// <nodoc />
        public string Id { get; }

        /// <nodoc />
        public string Version { get; }

        /// <nodoc />
        [CanBeNull]
        public string Alias { get; }

        /// <nodoc />
        public PackageIdentity(string protocol, string id, string version, [CanBeNull]string alias)
        {
            Contract.Requires(protocol != null);
            Contract.Requires(id != null);
            Contract.Requires(version != null);

            Protocol = protocol;
            Id = id;
            Version = version;
            Alias = alias;
        }

        /// <nodoc />
        public static PackageIdentity Nuget(string id, string version, string alias) => new PackageIdentity("nuget", id, version, alias);

        /// <summary>
        /// Returns a friendly name for a current package.
        /// </summary>
        public string GetFriendlyName()
            => FormattableStringEx.I($"{Protocol}://{Id}/{Version}");

        /// <inheritdoc />
        public bool Equals(PackageIdentity other)
        {
            return string.Equals(Protocol, other.Protocol) && string.Equals(Id, other.Id) && string.Equals(Version, other.Version) && string.Equals(Alias, other.Alias);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is PackageIdentity && Equals((PackageIdentity) obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Protocol.GetHashCode(), Id.GetHashCode(), Version.GetHashCode(), Alias?.GetHashCode() ?? 0);
        }

        /// <nodoc />
        public static bool operator ==(PackageIdentity left, PackageIdentity right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(PackageIdentity left, PackageIdentity right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Set of possible sources for <see cref="PackageDownloadResult"/>.
    /// </summary>
    public enum PackageSource
    {
        /// <summary>
        /// The package came from disk.
        /// </summary>
        Disk,

        /// <summary>
        /// The package came from the cache.
        /// </summary>
        Cache,

        /// <summary>
        /// The package came from a remote store (like nuget). 
        /// </summary>
        RemoteStore,
    }

    /// <summary>
    /// The result of downloaded package either from the cache or from the package manager.
    /// </summary>
    public sealed class PackageDownloadResult
    {
        // TODO: add readonly once switched to a recent compiler with readonly structs.
        private PackageIdentity m_packageIdentity;

        /// <nodoc />
        public string Protocol => m_packageIdentity.Protocol;

        /// <nodoc />
        public string Id => m_packageIdentity.Id;

        /// <nodoc />
        public string Version => m_packageIdentity.Version;

        /// <nodoc />
        public AbsolutePath TargetLocation { get; }

        /// <nodoc />
        public IReadOnlyList<RelativePath> Contents { get; }

        /// <nodoc />
        public PackageSource Source { get; }

        /// <summary>
        /// True if the generated spec's format hasn't been changed.
        /// </summary>
        /// <remarks>
        /// Property should be used only when <see cref="Source"/> is PackageSource.FromDisk.
        /// </remarks>
        public bool SpecsFormatIsUpToDate { get; }

        /// <nodoc />
        public bool IsValid => Contents.Count != 0 && TargetLocation.IsValid;

        /// <nodoc />
        public PackageDownloadResult(
            PackageIdentity packageIdentity,
            AbsolutePath targetLocation,
            IReadOnlyList<RelativePath> contents,
            PackageSource source,
            bool specsFormatIsUpToDate = false)
        {
            m_packageIdentity = packageIdentity;
            TargetLocation = targetLocation;
            Contents = contents;
            Source = source;
            SpecsFormatIsUpToDate = specsFormatIsUpToDate;
        }

        /// <nodoc />
        public static PackageDownloadResult RecoverableError(PackageIdentity packageIdentity) => FromCache(
            packageIdentity,
            AbsolutePath.Invalid,
            CollectionUtilities.EmptyArray<RelativePath>());

        /// <nodoc />
        public static PackageDownloadResult FromDisk(
            PackageIdentity packageIdentity,
            AbsolutePath targetLocation,
            IReadOnlyList<RelativePath> contents,
            bool specsFormatIsUpToDate)
            => new PackageDownloadResult(packageIdentity, targetLocation, contents, PackageSource.Disk, specsFormatIsUpToDate);
        
        /// <nodoc />
        public static PackageDownloadResult FromCache(
            PackageIdentity packageIdentity,
            AbsolutePath targetLocation,
            IReadOnlyList<RelativePath> contents)
            => new PackageDownloadResult(packageIdentity, targetLocation, contents, PackageSource.Cache);
        
        /// <nodoc />
        public static PackageDownloadResult FromRemote(
            PackageIdentity packageIdentity,
            AbsolutePath targetLocation,
            IReadOnlyList<RelativePath> contents)
            => new PackageDownloadResult(packageIdentity, targetLocation, contents, PackageSource.RemoteStore);
    }
}
