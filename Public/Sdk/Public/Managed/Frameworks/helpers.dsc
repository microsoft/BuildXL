import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import {DotNetCoreVersion} from "Sdk.Managed.Shared";

namespace Helpers {
    export declare const qualifier : {};

    @@public
    export function getDotNetCoreToolTemplate(version: DotNetCoreVersion) : Transformer.ExecuteArgumentsComposible {
        const host = Context.getCurrentHost();
        
        Contract.assert(host.cpuArchitecture === "x64", "The current DotNetCore Runtime package only has x64 version of Node. Ensure this runs on a 64-bit OS -or- update PowerShell.Core package to have other architectures embedded and fix this logic");

        const executable = host.os === 'win' ? r`dotnet.exe` : r`dotnet`;
        const pkgContents  = getRuntimePackagesContent(version, host);

        return {
            tool: {
                exe: pkgContents.assertExistence(executable), 
                dependsOnCurrentHostOSDirectories: true
            },
            dependencies: [
                pkgContents
            ],
            environmentVariables: [
                // Make sure DotNet core runs isolated from the framework your build selected and doesn't go off reading registry and dependd on globally installed tools to make the build unreliable
                // https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet?tabs=netcore21
                { name: "DOTNET_MULTILEVEL_LOOKUP", value: "0" }, 

                // Speed up dotnet core by preventing it from doing all kinds of startup logic like pulling packages.
                // https://docs.microsoft.com/en-us/dotnet/core/tools/global-tools
                { name: "DOTNET_SKIP_FIRST_TIME_EXPERIENCE", value: "1" } 
            ]
        };
    }

    function getRuntimePackagesContent(version: DotNetCoreVersion, host: Context.CurrentHostInformation) : StaticDirectory {
        if (version === 'netcoreapp3.1')
        {
            switch (host.os) {
                case "win":
                    return importFrom("DotNet-Runtime.win-x64").extracted;
                case "macOS":
                    return importFrom("DotNet-Runtime.osx-x64").extracted;
                case "unix":
                    return importFrom("DotNet-Runtime.linux-x64").extracted;
                default:
                    Contract.fail(`The current DotNetCore Runtime package doesn't support the current target runtime: ${host.os}. Ensure you run on a supported OS -or- update the DotNet-Runtime package to have the version embdded.`);
            }
        }
        
        if (version === 'net5.0')
        {
            switch (host.os) {
                case "win":
                    return importFrom("DotNet-Runtime-5.win-x64").extracted;
                case "macOS":
                    return importFrom("DotNet-Runtime-5.osx-x64").extracted;
                case "unix":
                    return importFrom("DotNet-Runtime-5.linux-x64").extracted;
                default:
                    Contract.fail(`The current DotNetCore Runtime package doesn't support the current target runtime: ${host.os}. Ensure you run on a supported OS -or- update the DotNet-Runtime package to have the version embdded.`);
            }
        }
        
        if (version === 'net6.0')
        {
            switch (host.os) {
                case "win":
                    return importFrom("DotNet-Runtime-6.win-x64").extracted;
                case "macOS":
                    return importFrom("DotNet-Runtime-6.osx-x64").extracted;
                case "unix":
                    return importFrom("DotNet-Runtime-6.linux-x64").extracted;
                default:
                    Contract.fail(`The current DotNetCore Runtime package doesn't support the current target runtime: ${host.os}. Ensure you run on a supported OS -or- update the DotNet-Runtime package to have the version embdded.`);
            }
        }
        
        Contract.fail(`Unsupport .NET Core version ${version}.`);
    }

    @@public
    export function getDotNetToolTemplate(version: DotNetCoreVersion) : Transformer.ExecuteArgumentsComposible {
        return getDotNetCoreToolTemplate(version);
    }

    const toolTemplate = getDotNetCoreToolTemplate("netcoreapp3.1");

    const tool5Template = getDotNetCoreToolTemplate("net5.0");

    const tool6Template = getDotNetCoreToolTemplate("net6.0");

    function getCachedDotNetCoreToolTemplate(dotNetCoreVersion: DotNetCoreVersion) {
        switch (dotNetCoreVersion) {
            case "netcoreapp3.1": return toolTemplate;
            case "net5.0": return tool5Template;
            case "net6.0": return tool6Template;
            default: Contract.fail(`Unknown .NET Core version '${dotNetCoreVersion}'.`);
        }
    }

    @@public
    export function wrapInDotNetExeForCurrentOs(dotNetCoreVersion: DotNetCoreVersion, args: Transformer.ExecuteArguments) : Transformer.ExecuteArguments {
        return Object.merge<Transformer.ExecuteArguments>(
            args,
            getCachedDotNetCoreToolTemplate(dotNetCoreVersion),
            {
                arguments: [
                    Cmd.argument(Artifact.input(args.tool.exe))
                ].prependWhenMerged()
            });
    }
}