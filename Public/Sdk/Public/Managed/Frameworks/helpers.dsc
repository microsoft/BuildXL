import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

namespace Helpers
{
    export declare const qualifier : {};

    @@public
    export function getDotNetToolTemplate() : Transformer.ExecuteArgumentsComposible {
        const host = Context.getCurrentHost();

        Contract.assert(host.cpuArchitecture === "x64", "The current DotNetCore Runtime package only has x64 version of Node. Ensure this runs on a 64-bit OS -or- update PowerShell.Core package to have other architectures embedded and fix this logic");

        let executable : RelativePath = undefined;
        let pkgContents : StaticDirectory = undefined;

        switch (host.os) {
            case "win":
                pkgContents = importFrom("DotNet-Runtime.win-x64").extracted;
                executable = r`dotnet.exe`;
                break;
            case "macOS":
                pkgContents = importFrom("DotNet-Runtime.osx-x64").extracted;
                executable = r`dotnet`;
                break;
            case "unix":
                pkgContents = importFrom("DotNet-Runtime.linux-x64").extracted;
                executable = r`dotnet`;
                break;
            default:
                Contract.fail(`The current DotNetCore Runtime package doesn't support the current target runtime: ${host.os}. Esure you run on a supported OS -or- update the DotNet-Runtime package to have the version embdded.`);
        }

        return {
            tool: {
                exe: pkgContents.getFile(executable),
                dependsOnCurrentHostOSDirectories: true,
            },
            dependencies: [
                pkgContents,
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

    const toolTemplate = getDotNetToolTemplate();

    @@public
    export function wrapInDotNetExeForCurrentOs(args: Transformer.ExecuteArguments) : Transformer.ExecuteArguments {
        return Object.merge<Transformer.ExecuteArguments>(
            args,
            toolTemplate,
            {
                arguments: [
                    Cmd.argument(Artifact.input(args.tool.exe))
                ].prependWhenMerged()
            });
    }
}