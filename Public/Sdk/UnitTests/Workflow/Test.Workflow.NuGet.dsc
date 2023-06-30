import {Transformer, Cmd, Artifact} from "Sdk.Transformers";
import {Assert, Testing} from "Sdk.Testing";
import * as Workflow from "Sdk.Workflow";

namespace Sdk.Tests {

    @@Testing.unitTest()
    export function runNuGetRestoreTask(){

        Testing.setMountPoint({ name: a`ProgramFiles`,    path: p`src/ProgramFiles`,    trackSourceFileChanges: true, isWritable: false, isReadable: true });
        Testing.setMountPoint({ name: a`ProgramData`,     path: p`src/ProgramData`,     trackSourceFileChanges: true, isWritable: true,  isReadable: true });
        Testing.setMountPoint({ name: a`LocalLow`,        path: p`src/LocalLow`,        trackSourceFileChanges: true, isWritable: true,  isReadable: true });
        Testing.setMountPoint({ name: a`ProgramFilesX86`, path: p`src/ProgramFilesX86`, trackSourceFileChanges: true, isWritable: false, isReadable: true });
        Testing.setMountPoint({ name: a`UserProfile`,     path: p`src/BuildXL`,         trackSourceFileChanges: true, isWritable: true,  isReadable: true });
        Testing.setBuildParameter("COMSPEC", d`src/cmd.exe`.toDiagnosticString());

        const task = Workflow.NuGet.restore({
            packages:               [
                                        {
                                            kind:           "NuGet",
                                            name:           "PackageA",
                                            version:        "1.0.1",
                                            directories:    [d`src/distrib/PackageA.1.0.1`]
                                        },
                                        {
                                            kind:           "NuGet",
                                            name:           "PackageB",
                                            version:        "2.0.1",
                                            directories:    [d`src/distrib/PackageB.2.0.1`]
                                        }
                                    ],
            targetFramework:        "net472",
            noWarns:                ["CS0436"],
            sourceRoot:             d`src/distrib`,
            feeds:                  [{ name: "MyFeed", location: "https://myfeed.pkgs.visualstudio.com/DefaultCollection/_packaging/MyFeed/nuget/v3/index.json" }],
            restoreDirectory:       d`out/restore`
        });
        Assert.isTrue(pathInTaskOutputs(p`out/restore`, task));
    }

    /////// Temporarily duplication from Test.Workflow.dsc because TestRunner doesn't support referencing other .dsc.
    /////// TODO: Remove this duplication once TestRunner supports referencing other .dsc.

    /** Checks if a path is in an array of outputs of files or static directories. */
    function pathInOutputs(path: Path, outputs: (File | StaticDirectory)[]) : boolean
    {
        const paths = outputs.map(o => typeof(o) === "StaticDirectory" ? (<StaticDirectory>o).root.path : (<File>o).path);
        return paths.some(p => p === path);
    }

    /** Checks if a path is in one of the specified task outputs. */
    function pathInTaskOutputs(path: Path, ...tasks: Workflow.TaskOutput[]) : boolean
    {
        return pathInOutputs(path, tasks.mapMany(t => t.taskOutputs));
    }
}
