import * as MySdk from "MySdk";

const includeDir = Transformer.sealSourceDirectory(d`../../Include`, Transformer.SealSourceDirectoryOption.allDirectories);

@@public
export const obj = MySdk.compile({
    cFile: f`hello.cpp`,
    includes: [f`greetings.h`],
    includeSearchDirs: [includeDir],
    optimize: true
});