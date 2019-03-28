// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "Sdk.Managed.Frameworks.Net451",
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [
        f`net451.dsc`,
        f`netFx.dsc`,
    ]
});
