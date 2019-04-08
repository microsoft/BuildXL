// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

// Example how to build an executable using Managed
export const exe = Managed.executable({
    assemblyName: "HelloWorld",
    sources: [ f`HelloWorld.cs` ],
    references: [Managed.StandardAssemblies.MsCorLib.dll],
    skipDocumentationGeneration: true
});
