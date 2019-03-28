// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "Sdk.Managed.Frameworks.Net461",
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [
        f`net461.dsc`,
        f`netFx.dsc`,
    ]
});
