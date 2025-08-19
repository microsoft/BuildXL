// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "BuildXL.Tools.SymbolDaemon",
    projects: [
        f`Tool.SymbolDaemon.Deployment.dsc`,
        f`Tool.SymbolDaemon.dsc`,
        f`Tool.SymbolDaemonInterfaces.dsc`,
        f`Tool.SymbolDaemonRunner.dsc`,
        ...addIf(Environment.getFlag("[Sdk.BuildXL]microsoftInternal"), f`Tool.SymbolDaemon.Tool.dsc`),
        ...addIf(!Environment.getFlag("[Sdk.BuildXL]microsoftInternal"), f`Tool.SymbolDaemon.Tool.Public.dsc`)
    ]
});
