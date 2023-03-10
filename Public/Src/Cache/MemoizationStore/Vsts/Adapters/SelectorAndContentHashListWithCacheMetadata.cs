// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Vsts.Adapters;

/// <summary>
/// A Data class for a Selector and ContentHashList.
/// </summary>
public readonly record struct SelectorAndContentHashListWithCacheMetadata(Selector Selector, ContentHashListWithCacheMetadata ContentHashList);
