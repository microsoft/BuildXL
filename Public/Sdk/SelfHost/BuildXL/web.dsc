import * as Shared from "Sdk.Managed.Shared";

namespace WebFramework {
    
    export declare const qualifier : Shared.TargetFrameworks.CoreClr;

    @@public
    export function getFrameworkPackage() : Shared.ManagedNugetPackage {
        Contract.assert(isDotNetCoreApp);
        return Shared.Factory.createFrameworkPackage(
            isDotNetCore31() ? importFrom("Microsoft.AspNetCore.App.Ref").pkg : importFrom("Microsoft.AspNetCore.App.Ref.5.0.0").pkg,
            getRuntimePackage(),
            a`${qualifier.targetRuntime}`,
            a`${qualifier.targetFramework}`
        );
    }

    function getRuntimePackage() : NugetPackage {
        switch (qualifier.targetRuntime) {
            case "win-x64":
                return isDotNetCore31() ? importFrom("Microsoft.AspNetCore.App.Runtime.win-x64").pkg : importFrom("Microsoft.AspNetCore.App.Runtime.win-x64.5.0.0").pkg;
            case "osx-x64":
                return isDotNetCore31() ? importFrom("Microsoft.AspNetCore.App.Runtime.osx-x64").pkg : importFrom("Microsoft.AspNetCore.App.Runtime.osx-x64.5.0.0").pkg;
            case "linux-x64":
                return isDotNetCore31() ? importFrom("Microsoft.AspNetCore.App.Runtime.linux-x64").pkg : importFrom("Microsoft.AspNetCore.App.Runtime.linux-x64.5.0.0").pkg;
            default:
                Contract.fail("Unsupported target framework");
        }
    }

    function isDotNetCore31() : boolean {
        return qualifier.targetFramework === "netcoreapp3.1";
    }
}