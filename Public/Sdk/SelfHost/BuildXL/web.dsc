import * as Shared from "Sdk.Managed.Shared";

namespace WebFramework {
    
    export declare const qualifier : Shared.TargetFrameworks.CoreClr;

    @@public
    export function getFrameworkPackage() : Shared.ManagedNugetPackage {
        Contract.assert(isDotNetCoreApp);
        return Shared.Factory.createFrameworkPackage(
            importPackage(() => importFrom("Microsoft.AspNetCore.App.Ref.6.0.0").pkg),
            getRuntimePackage(),
            a`${qualifier.targetRuntime}`,
            a`${qualifier.targetFramework}`
        );
    }

    function getRuntimePackage() : NugetPackage {
        switch (qualifier.targetRuntime) {
            case "win-x64":
                return importPackage(() => importFrom("Microsoft.AspNetCore.App.Runtime.win-x64.6.0.0").pkg);
            case "osx-x64":
                return importPackage(() => importFrom("Microsoft.AspNetCore.App.Runtime.osx-x64.6.0.0").pkg);
            case "linux-x64":
                return importPackage(() => importFrom("Microsoft.AspNetCore.App.Runtime.linux-x64.6.0.0").pkg);
            default:
                Contract.fail("Unsupported target framework");
        }
    }

    function importPackage(net60: () => Shared.ManagedNugetPackage) : Shared.ManagedNugetPackage {
        switch (qualifier.targetFramework) {
            case "net6.0": return net60();
            default: Contract.fail(`Unsupported target framework ${qualifier.targetFramework}.`);
        }
    }
}