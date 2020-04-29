namespace CloudBuildDropHelper {
  
    const enabled = Environment.hasVariable("BUILDXL_DROP_ENABLED") ? Environment.getBooleanValue("BUILDXL_DROP_ENABLED") : false;
    
    /** The runner that preforms the upload */
    const runner = enabled ? DropDaemonRunner.withQualifier({configuration: "release", targetFramework: "net472", targetRuntime: "win-x64"}).cloudBuildRunner : undefined;

    /** The settings for this drop */
    const settings = {
        dropServiceConfigFile: Environment.getFileValue("BUILDXL_DROP_CONFIG")
    };

    /** The drop create result to use for all uploads */
    const createResult = enabled ? runner.createDrop(settings) : undefined;

    /**
     * Adds directories to drop using the CloudBuild configured runner
     */
    @@public
    export function addDirectoriesToDrop(
            outputs: (StaticDirectory | DirectoryInfo)[],
            args?: DropOperationArguments) : Result
    {
        if (!enabled)
        {
            return;
        }

        const dropArgs = args || {};

        const dirInfos : DirectoryInfo[] = outputs.map(output => output["__staticDirectoryBrand"] ? <DirectoryInfo>{directory: output} : <DirectoryInfo> output);

        return runner.addDirectoriesToDrop(createResult, dropArgs, dirInfos);
    };
}
