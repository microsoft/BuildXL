// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.Interfaces.Sessions;

/// <summary>
/// Container for a individual member of BulkPlace call
/// </summary>
public readonly record struct ContentHashWithPath(ContentHash Hash, AbsolutePath Path);
