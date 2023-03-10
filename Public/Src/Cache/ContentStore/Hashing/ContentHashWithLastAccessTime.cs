// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;

namespace BuildXL.Cache.ContentStore.Hashing;

/// <summary>
/// Pairing of content hash and last access time.
/// </summary>
public readonly record struct ContentHashWithLastAccessTime(ContentHash Hash, DateTime LastAccessTime);
