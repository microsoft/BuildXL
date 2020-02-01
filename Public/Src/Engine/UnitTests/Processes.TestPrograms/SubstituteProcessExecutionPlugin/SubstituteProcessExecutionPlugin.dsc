import * as Native from "Sdk.Native";
import {Transformer} from "Sdk.Transformers";

namespace Processes.TestPrograms.SubstituteProcessExecutionPlugin {
    export declare const qualifier: BuildXLSdk.PlatformDependentQualifier;

    export const dll: Native.Dll.NativeDllImage = Context.getCurrentHost().os === "win" && Native.Dll.build({
        outputFileName: PathAtom.create("SubstituteProcessExecutionPlugin.dll"),
        sources: [ f`dllmain.cpp` ],
        includes: [
            importFrom("WindowsSdk").UM.include,
            importFrom("WindowsSdk").Shared.include,
            importFrom("WindowsSdk").Ucrt.include,
            importFrom("VisualCpp").include,
        ],
        libraries: [
            ...importFrom("WindowsSdk").UM.standardLibs,
            importFrom("VisualCpp").lib,
            importFrom("WindowsSdk").Ucrt.lib,
        ]
    });
}