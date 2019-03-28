import {Transformer} from "Sdk.Transformers";

namespace Managed {
    @@public
    export const assemblyVersion = "1.0.0.0";

    @@public
    export const safeFileVersion = assemblyVersion; // we only rev the fileversion of the main executable to maintain incremetal builds.

    @@public
    export const fileVersion = explicitSemanticVersion + ".0"; // The main file version of bxl.exe
}
