// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

const aspVersion = "7.0.0";

// Versions used by framework reference packages for reference assemblies
// and runtime assemblies respectively
const asp6RefVersion = "6.0.22";
const asp6RuntimeVersion = "6.0.22";

const asp7RefVersion = "7.0.11";
const asp7RuntimeVersion = "7.0.11";

export const pkgs = [
    // aspnet web api
    { id: "Microsoft.AspNet.WebApi.Client", version: "5.2.7" },
    { id: "Microsoft.AspNet.WebApi.Core", version: "5.2.3" },
    { id: "Microsoft.AspNet.WebApi.WebHost", version: "5.2.2" },

    // aspnet core
    { id: "Microsoft.Extensions.Configuration.Abstractions", version: aspVersion },
    { id: "Microsoft.Extensions.Configuration.Binder", version: aspVersion },
    { id: "Microsoft.Extensions.Configuration", version: aspVersion },
    { id: "Microsoft.Extensions.DependencyInjection.Abstractions", version: aspVersion },
    { id: "Microsoft.Extensions.Logging", version: aspVersion },
    { id: "Microsoft.Extensions.DependencyInjection", version: aspVersion },
    { id: "Microsoft.Extensions.Options", version: aspVersion },
    { id: "Microsoft.Extensions.Primitives", version: aspVersion },

    { id: "Microsoft.Net.Http", version: "2.2.29" },

    { id: "Microsoft.AspNetCore.App.Ref", version: asp6RefVersion, alias: "Microsoft.AspNetCore.App.Ref.6.0.0" },
    
    // Microsoft.AsptNetCore.App.Runtime.* packages embed some packages we also consume 
    // directly. Exclude all these packages, and use the proper nuget package directly as needed
    { id: "Microsoft.AspNetCore.App.Runtime.win-x64", version: asp6RuntimeVersion, alias: "Microsoft.AspNetCore.App.Runtime.win-x64.6.0.0", 
        filesToExclude: [r`runtimes/win-x64/lib/net6.0/Microsoft.Extensions.Logging.Abstractions.dll`, r`runtimes/win-x64/lib/net6.0/Microsoft.Extensions.Logging.dll`]},
    { id: "Microsoft.AspNetCore.App.Runtime.linux-x64", version: asp6RuntimeVersion, alias: "Microsoft.AspNetCore.App.Runtime.linux-x64.6.0.0", 
        filesToExclude: [r`runtimes/linux-x64/lib/net6.0/Microsoft.Extensions.Logging.Abstractions.dll`, r`runtimes/linux-x64/lib/net6.0/Microsoft.Extensions.Logging.dll`] },
    { id: "Microsoft.AspNetCore.App.Runtime.osx-x64", version: asp6RuntimeVersion, alias: "Microsoft.AspNetCore.App.Runtime.osx-x64.6.0.0", 
        filesToExclude: [r`runtimes/osx-x64/lib/net6.0/Microsoft.Extensions.Logging.Abstractions.dll`, r`runtimes/osx-x64/lib/net6.0/Microsoft.Extensions.Logging.dll`] },

    { id: "Microsoft.AspNetCore.App.Ref", version: asp7RefVersion, alias: "Microsoft.AspNetCore.App.Ref.7.0.0" },
    { id: "Microsoft.AspNetCore.App.Runtime.win-x64", version: asp7RuntimeVersion, alias: "Microsoft.AspNetCore.App.Runtime.win-x64.7.0.0", 
        filesToExclude: [r`runtimes/win-x64/lib/net7.0/Microsoft.Extensions.Logging.Abstractions.dll`, r`runtimes/win-x64/lib/net7.0/Microsoft.Extensions.Logging.dll`] },
    { id: "Microsoft.AspNetCore.App.Runtime.linux-x64", version: asp7RuntimeVersion, alias: "Microsoft.AspNetCore.App.Runtime.linux-x64.7.0.0", 
        filesToExclude: [r`runtimes/linux-x64/lib/net7.0/Microsoft.Extensions.Logging.Abstractions.dll`, r`runtimes/linux-x64/lib/net7.0/Microsoft.Extensions.Logging.dll`] },
    { id: "Microsoft.AspNetCore.App.Runtime.osx-x64", version: asp7RuntimeVersion, alias: "Microsoft.AspNetCore.App.Runtime.osx-x64.7.0.0", 
        filesToExclude: [r`runtimes/osx-x64/lib/net7.0/Microsoft.Extensions.Logging.Abstractions.dll`, r`runtimes/osx-x64/lib/net7.0/Microsoft.Extensions.Logging.dll`] },
];
