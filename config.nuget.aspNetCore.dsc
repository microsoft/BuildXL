// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

const aspVersion = "9.0.0";

// Versions used by framework reference packages for reference assemblies
// and runtime assemblies respectively
const asp8RefVersion = "8.0.21";
const asp8RuntimeVersion = "8.0.21";

const asp9RefVersion = "9.0.10";
const asp9RuntimeVersion = "9.0.10";

export const pkgs = [
    // aspnet web api
    { id: "Microsoft.AspNet.WebApi.Client", version: "5.2.7" },
    { id: "Microsoft.AspNet.WebApi.Core", version: "5.2.7" },
    { id: "Microsoft.AspNet.WebApi.WebHost", version: "5.2.7" },

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
    
    // Microsoft.AsptNetCore.App.Runtime.* packages embed some packages we also consume 
    // directly. Exclude all these packages, and use the proper nuget package directly as needed
    { id: "Microsoft.AspNetCore.App.Ref", version: asp8RefVersion, alias: "Microsoft.AspNetCore.App.Ref.8.0.0" },
    { id: "Microsoft.AspNetCore.App.Runtime.win-x64", version: asp8RuntimeVersion, alias: "Microsoft.AspNetCore.App.Runtime.win-x64.8.0.0", 
        filesToExclude: [r`runtimes/win-x64/lib/net8.0/Microsoft.Extensions.Logging.Abstractions.dll`, r`runtimes/win-x64/lib/net8.0/Microsoft.Extensions.Logging.dll`] },
    { id: "Microsoft.AspNetCore.App.Runtime.linux-x64", version: asp8RuntimeVersion, alias: "Microsoft.AspNetCore.App.Runtime.linux-x64.8.0.0", 
        filesToExclude: [r`runtimes/linux-x64/lib/net8.0/Microsoft.Extensions.Logging.Abstractions.dll`, r`runtimes/linux-x64/lib/net8.0/Microsoft.Extensions.Logging.dll`] },
    { id: "Microsoft.AspNetCore.App.Runtime.osx-x64", version: asp8RuntimeVersion, alias: "Microsoft.AspNetCore.App.Runtime.osx-x64.8.0.0", 
        filesToExclude: [r`runtimes/osx-x64/lib/net8.0/Microsoft.Extensions.Logging.Abstractions.dll`, r`runtimes/osx-x64/lib/net8.0/Microsoft.Extensions.Logging.dll`] },

    { id: "Microsoft.AspNetCore.App.Ref", version: asp9RefVersion, alias: "Microsoft.AspNetCore.App.Ref.9.0.0" },
    { id: "Microsoft.AspNetCore.App.Runtime.win-x64", version: asp9RuntimeVersion, alias: "Microsoft.AspNetCore.App.Runtime.win-x64.9.0.0", 
        filesToExclude: [r`runtimes/win-x64/lib/net9.0/Microsoft.Extensions.Logging.Abstractions.dll`, r`runtimes/win-x64/lib/net9.0/Microsoft.Extensions.Logging.dll`] },
    { id: "Microsoft.AspNetCore.App.Runtime.linux-x64", version: asp9RuntimeVersion, alias: "Microsoft.AspNetCore.App.Runtime.linux-x64.9.0.0", 
        filesToExclude: [r`runtimes/linux-x64/lib/net9.0/Microsoft.Extensions.Logging.Abstractions.dll`, r`runtimes/linux-x64/lib/net9.0/Microsoft.Extensions.Logging.dll`] },
    { id: "Microsoft.AspNetCore.App.Runtime.osx-x64", version: asp9RuntimeVersion, alias: "Microsoft.AspNetCore.App.Runtime.osx-x64.9.0.0", 
        filesToExclude: [r`runtimes/osx-x64/lib/net9.0/Microsoft.Extensions.Logging.Abstractions.dll`, r`runtimes/osx-x64/lib/net9.0/Microsoft.Extensions.Logging.dll`] },
];
