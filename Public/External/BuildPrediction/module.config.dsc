// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    // TODO: this name is clashing with MsBuild module (called "Microsoft.Build" as well). And because of the order among resolvers
    // this module is not being built under BuildXL!
    name: "Microsoft.Build.Prediction",
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences
});
