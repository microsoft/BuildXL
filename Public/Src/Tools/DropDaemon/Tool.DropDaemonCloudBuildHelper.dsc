import {Transformer} from "Sdk.Transformers";

namespace CloudBuildDropHelper {
  
    const enabled = Environment.hasVariable("BUILDXL_DROP_ENABLED") ? Environment.getBooleanValue("BUILDXL_DROP_ENABLED") : false;

    /** The runner that preforms the upload */
    const runner = enabled ? DropDaemonRunner.withQualifier({configuration: "release", targetFramework: "net472", targetRuntime: "win-x64"}).cloudBuildRunner : undefined;

    /** The settings for this drop */
    const settings = {
        dropServiceConfigFile: Environment.getFileValue("BUILDXL_DROP_CONFIG")
    };

    /** 
     * Creates a drop using the CloudBuild configured settings.
     * If drop is not enabled in CloudBuild, returns undefined.
     */
    @@public
    export function createDrop() : DropCreateResult {
        return enabled ? runner.createDrop(settings) : undefined;
    }

    /**
     * Adds directories to drop using the CloudBuild configured runner.
     * Does nothing and returns undefined if drop is not enabled in CloudBuild.
     */
    @@public
    export function addDirectoriesToDrop(
            drop: DropCreateResult,
            outputs: (StaticDirectory | DirectoryInfo)[],
            args?: DropOperationArguments) : Result
    {
        if (!enabled || !drop)
        {
            return undefined;
        }

        const dropArgs = args || {};

        const dirInfos : DirectoryInfo[] = outputs.map(output => Transformer.isStaticDirectory(output) ? {directory: output, dropPath: r`.`} : output);

        return runner.addDirectoriesToDrop(drop, dropArgs, dirInfos);
    };
}
