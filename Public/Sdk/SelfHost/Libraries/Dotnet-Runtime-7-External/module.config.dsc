// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// These are the external versions of the architecture-specific DotNet-Runtime packages.

module({
    name: "DotNet-Runtime-7.win-x64",
    projects: [f`DotNet-Runtime.win-x64.dsc`]
});

module({
    name: "DotNet-Runtime-7.osx-x64",
    projects: [f`DotNet-Runtime.osx-x64.dsc`]
});

module({
    name: "DotNet-Runtime-7.linux-x64",
    projects: [f`DotNet-Runtime.linux-x64.dsc`]
});