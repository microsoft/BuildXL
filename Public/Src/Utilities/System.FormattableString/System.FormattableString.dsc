// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace System.FormattableString {
    // This project allows to use Formattable string with any .NET Frameworks and avoid build breaks when
    // the BuildXL assemblies are referenced directly in an environment that targets .NET 4.6+.
    // When BuildXL codebase will move to 4.6 or .NET Core, this project can be easily removed and that's the only change that will be needed.
    @@public
    export const dll = (qualifier.targetFramework !== "net451") ? undefined : BuildXLSdk.library({
        assemblyName: "System.FormattableString",
        skipDefaultReferences: true,
        sources: globR(d`.`, "*.cs"),
    });
}
