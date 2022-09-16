// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

const aspVersion = "2.2.0";

// Versions used by framework reference packages for reference assemblies
// and runtime assemblies respectively
const aspRefVersion = "3.1.10";
const aspRuntimeVersion = "3.1.24";

const asp5RefVersion = "5.0.0";
const asp5RuntimeVersion = "5.0.17";

const asp6RefVersion = "6.0.9";
const asp6RuntimeVersion = "6.0.9";

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
    { id: "Microsoft.Extensions.Logging.Abstractions", version: aspVersion },
    { id: "Microsoft.Extensions.Logging", version: aspVersion },
    { id: "Microsoft.Extensions.Options", version: aspVersion },
    { id: "Microsoft.Extensions.Primitives", version: aspVersion },

    { id: "Microsoft.Net.Http", version: "2.2.29" },

    { id: "Microsoft.AspNetCore.App.Ref", version: aspRefVersion },
    { id: "Microsoft.AspNetCore.App.Runtime.win-x64", version: aspRuntimeVersion },
    { id: "Microsoft.AspNetCore.App.Runtime.linux-x64", version: aspRuntimeVersion },
    { id: "Microsoft.AspNetCore.App.Runtime.osx-x64", version: aspRuntimeVersion },

    { id: "Microsoft.AspNetCore.App.Ref", version: asp5RefVersion, alias: "Microsoft.AspNetCore.App.Ref.5.0.0" },
    { id: "Microsoft.AspNetCore.App.Runtime.win-x64", version: asp5RuntimeVersion, alias: "Microsoft.AspNetCore.App.Runtime.win-x64.5.0.0" },
    { id: "Microsoft.AspNetCore.App.Runtime.linux-x64", version: asp5RuntimeVersion, alias: "Microsoft.AspNetCore.App.Runtime.linux-x64.5.0.0" },
    { id: "Microsoft.AspNetCore.App.Runtime.osx-x64", version: asp5RuntimeVersion, alias: "Microsoft.AspNetCore.App.Runtime.osx-x64.5.0.0" },

    { id: "Microsoft.AspNetCore.App.Ref", version: asp6RefVersion, alias: "Microsoft.AspNetCore.App.Ref.6.0.0" },
    { id: "Microsoft.AspNetCore.App.Runtime.win-x64", version: asp6RuntimeVersion, alias: "Microsoft.AspNetCore.App.Runtime.win-x64.6.0.0" },
    { id: "Microsoft.AspNetCore.App.Runtime.linux-x64", version: asp6RuntimeVersion, alias: "Microsoft.AspNetCore.App.Runtime.linux-x64.6.0.0" },
    { id: "Microsoft.AspNetCore.App.Runtime.osx-x64", version: asp6RuntimeVersion, alias: "Microsoft.AspNetCore.App.Runtime.osx-x64.6.0.0" },
];
