// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// This is basically a function that maps a key to a location (i.e., a map).
/// </summary>
public interface IShardingScheme<TKey, TLoc>
{
    /// <summary>
    /// Returns the location for the given key.
    /// </summary>
    /// <remarks>
    /// Locations are guaranteed to exist. However, they may not be available. See <see cref="IShardManager{TLoc}"/>.
    ///
    /// Whether a shard is returned or not is dependent on the sharding scheme. For example, a sharding scheme may
    /// assume that shards are always available, and thus always return a shard. Another sharding scheme may
    /// incorporate information about the state of shards, and thus return null if the shard is not available.
    /// </remarks>
    public Shard<TLoc>? Locate(TKey key);
}

/// <summary>
/// Same as <see cref="IShardingScheme{TKey,TLoc}"/>, but allows for multiple locations to be returned.
/// </summary>
public interface IMultiCandidateShardingScheme<TKey, TLoc>: IShardingScheme<TKey, TLoc>
{
    /// <summary>
    /// Obtain a set of locations responsible for a given key.
    /// </summary>
    /// <remarks>
    /// Locations are guaranteed to exist. However, they may not be available. See <see cref="IShardManager{TLoc}"/>.
    /// There may also not be enough locations to satisfy the requested number of candidates.
    /// 
    /// This is used to have fallback mechanisms. The intent is that the first location is the primary one, and the
    /// rest are fallbacks.
    ///
    /// Whether a shard is returned or not is dependent on the sharding scheme. For example, a sharding scheme may
    /// assume that shards are always available, and thus always return a shard. Another sharding scheme may
    /// incorporate information about the state of shards, and thus return null if the shard is not available.
    ///
    /// The traversal order of the returned locations is determined by the specific implementor of the interface, and
    /// is expected to be deterministic.
    /// </remarks>
    public IReadOnlyCollection<Shard<TLoc>> Locate(TKey key, int candidates);
}

/// <summary>
/// Used to abstract over the object that stores a location.
/// </summary>
/// <remarks>
/// Particular sharding schemes may produce additional information. Such information can be retrieved by casting the
/// object to the known type.
/// </remarks>
public record Shard<TLoc>(TLoc Location)
{
    public static implicit operator Shard<TLoc>(TLoc location) => new(location);

    public static implicit operator TLoc(Shard<TLoc> shard) => shard.Location;
}

/// <summary>
/// Used in <see cref="IShardManager{TLoc}"/>
/// </summary>
public interface ILocation<out TLoc>
{
    public TLoc Location { get; }

    public bool Available { get; }
}

/// <summary>
/// Allows abstracting over the shards that are available. This is used to allow for live resharding while a cluster is
/// still running. For example, if a node is being removed, it can be marked as unavailable, and the sharding scheme
/// can adjust for this.
/// </summary>
public interface IShardManager<out TLoc>
{
    public IReadOnlyList<ILocation<TLoc>> Locations { get; }

    /// <summary>
    /// When the number of nodes or their availabity changes, this event is triggered. This is useful for sharding
    /// schemes that need to compute ad-hoc data structures based on the locations.
    /// </summary>
    public event EventHandler OnResharding;
}
