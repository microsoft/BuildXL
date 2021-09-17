import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

namespace Helpers
{
    export declare const qualifier : {};

    @@public
    export function getDotNetCoreToolTemplate(isDotNet5: boolean) : Transformer.ExecuteArgumentsComposible {
        const host = Context.getCurrentHost();
        
        Contract.assert(host.cpuArchitecture === "x64", "The current DotNetCore Runtime package only has x64 version of Node. Ensure this runs on a 64-bit OS -or- update PowerShell.Core package to have other architectures embedded and fix this logic");

        let executable : RelativePath = undefined;
        let pkgContents : StaticDirectory = undefined;

        switch (host.os) {
            case "win":
                pkgContents = isDotNet5 ? importFrom("DotNet-Runtime-5.win-x64").extracted : importFrom("DotNet-Runtime.win-x64").extracted;
                executable = r`dotnet.exe`;
                break;
            case "macOS":
                pkgContents = isDotNet5 ? importFrom("DotNet-Runtime-5.osx-x64").extracted : importFrom("DotNet-Runtime.osx-x64").extracted;
                executable = r`dotnet`;
                break;
            case "unix":
                pkgContents = isDotNet5 ? importFrom("DotNet-Runtime-5.linux-x64").extracted : importFrom("DotNet-Runtime.linux-x64").extracted;
                executable = r`dotnet`;
                break;
            default:
                Contract.fail(`The current DotNetCore Runtime package doesn't support the current target runtime: ${host.os}. Esure you run on a supported OS -or- update the DotNet-Runtime package to have the version embdded.`);
        }

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

    @@public
    export function getDotNetToolTemplate(isDotNet5: boolean) : Transformer.ExecuteArgumentsComposible {
        return getDotNetCoreToolTemplate(isDotNet5);
    }

    const toolTemplate = getDotNetCoreToolTemplate(/*isDotNet5*/false);

    const tool5Template = getDotNetCoreToolTemplate(/*isDotNet5*/true);

    @@public
    export function wrapInDotNetExeForCurrentOs(isDotNet5: boolean, args: Transformer.ExecuteArguments) : Transformer.ExecuteArguments {
        return Object.merge<Transformer.ExecuteArguments>(
            args,
            isDotNet5 ? tool5Template : toolTemplate,
            {
                arguments: [
                    Cmd.argument(Artifact.input(args.tool.exe))
                ].prependWhenMerged()
            });
    }
}