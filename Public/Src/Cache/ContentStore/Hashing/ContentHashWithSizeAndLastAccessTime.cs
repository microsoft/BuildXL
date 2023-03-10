// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;

namespace BuildXL.Cache.ContentStore.Hashing;

/// <summary>
/// Pairing of content hash, size, and last access time.
/// </summary>
public readonly record struct ContentHashWithSizeAndLastAccessTime(ContentHash Hash, long Size, DateTime LastAccessTime);
