// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace EngineTestUtilities {
    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet6AndNet472;
    
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "Test.BuildXL.EngineTestUtilities",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Engine").Cache.dll,  
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Engine").Processes.External.dll,
            importFrom("BuildXL.Engine").ProcessPipExecutor.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Pips").dll,  
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Native.Extensions.dll,
            importFrom("BuildXL.Utilities").Ipc.dll, 
            importFrom("BuildXL.Utilities").Ipc.Providers.dll, 
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Utilities.UnitTests").TestUtilities.dll,
            importFrom("BuildXL.FrontEnd").Core.dll,
            importFrom("BuildXL.FrontEnd").Script.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
        ],
    });
}
