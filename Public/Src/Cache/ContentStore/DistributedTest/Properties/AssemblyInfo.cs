// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Required since ContentStoreDistributed.Versions is now visible in this assembly
#pragma warning disable 436

[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Microsoft.Performance", "CA1824:MarkAssembliesWithNeutralResourcesLanguage")]

// Test assemblies should not use 'ConfigureAwait(false)'
[assembly: DoNotUseConfigureAwait]
