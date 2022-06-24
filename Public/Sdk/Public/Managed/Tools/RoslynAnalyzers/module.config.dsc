// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "Sdk.Managed.Tools.RoslynAnalyzers",
    projects: [
        ...addIf(Environment.getFlag("[Sdk.BuildXL]microsoftInternal") && Context.getCurrentHost().os === "win", f`Tool.Guardian.RoslynAnalyzers.dsc`),
        ...addIf(!(Environment.getFlag("[Sdk.BuildXL]microsoftInternal") && Context.getCurrentHost().os === "win"), f`Tool.Guardian.RoslynAnalyzers.Public.dsc`)
    ]
});