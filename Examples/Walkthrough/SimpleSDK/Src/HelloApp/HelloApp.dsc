import * as MySdk from "MySdk";
import * as HelloLib from "HelloLib";

const includeDir = Transformer.sealSourceDirectory(d`../../Include`, Transformer.SealSourceDirectoryOption.allDirectories);

const obj = MySdk.compile({
    cFile: f`main.cpp`,
    includeSearchDirs: [includeDir],
    optimize: true
});

const exe = MySdk.link({
    objFiles: [obj, HelloLib.obj],
    output: p`${Context.getMount("ObjectRoot").path}/app.exe`
});