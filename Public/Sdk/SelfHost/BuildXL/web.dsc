
import * as Shared from "Sdk.Managed.Shared";

namespace WebFramework {
    
    export declare const qualifier : Shared.TargetFrameworks.CoreClr;

    @@public
    export function getFrameworkPackage() : Shared.ManagedNugetPackage {
        Contract.assert(qualifier.targetFramework === "netcoreapp3.1");
        return Shared.Factory.createFrameworkPackage(
            importFrom("Microsoft.AspNetCore.App.Ref").pkg,
            getRuntimePackage(),
            a`${qualifier.targetRuntime}`,
            a`${qualifier.targetFramework}`
        );
    }

    function getRuntimePackage() : NugetPackage {
        switch (qualifier.targetRuntime) {
            case "win-x64":
                return importFrom("Microsoft.AspNetCore.App.Runtime.win-x64").pkg;
            case "osx-x64":
                return importFrom("Microsoft.AspNetCore.App.Runtime.osx-x64").pkg;
            case "linux-x64":
                return importFrom("Microsoft.AspNetCore.App.Runtime.linux-x64").pkg;
            default:
                Contract.fail("Unsupported target framework");
        }
    }

}