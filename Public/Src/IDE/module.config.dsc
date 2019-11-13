// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "BuildXL.Ide", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [
        f`BuildXL.Ide.dsc`,
        f`Common/VersionUtilities.dsc`,
        f`Debugger/BuildXL.Ide.Script.Debugger.dsc`,
        f`Generator/BuildXL.Ide.Generator.dsc`,
        f`Generator.Old/BuildXL.Ide.Generator.Old.dsc`,
        f`LanguageServer/BuildXL.Ide.LanguageServer.dsc`,
        f`LanguageServerProtocol/LanguageServer/LanguageServer.dsc`,
        f`Shared/JsonRpc/JsonRpc.dsc`,
        ...globR(d`./VsCode`, "*.dsc"),
        ...globR(d`./UnitTests`, "*.dsc"),
    ]});