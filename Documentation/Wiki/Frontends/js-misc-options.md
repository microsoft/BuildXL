# Miscellaneous options

### keepProjectGraphFile: boolean
When true, a JSON representation of the build graph (as provided by the corresponding coordinator) is left on disk for debugging purposes. Defaults to false.

### blockWritesUnderNodeModules: boolean
When true, `node_modules` directory becomes read-only. This allows for extra build discipline enforcements, since tools are not supposed to write under this directory. This option is not on by default since there are some well-known tools that place cache folders under `node_modules`.

### doubleWritePolicy: DoubleWritePolicy
Defines the policy to apply when two pips write to the same file. By default, same-content writes are allowed, and different-content ones are blocked. The options are:

```typescript
type DoubleWritePolicy =
        // double writes are blocked
        "doubleWritesAreErrors" |
        // double writes are allowed as long as the file content is the same
        "allowSameContentDoubleWrites" |
        // double writes are allowed, and the first process writing the output will (non-deterministically)
        // win the race. Consider this will result in a non-deterministic deployment for a given build, and is therefore unsafe.
        "unsafeFirstDoubleWriteWins";
```

### writingToStandardErrorFailsExecution: boolean
When true, any tool that writes to standard error, even if the execution returns a successful exit code, will be interpreted as failed by BuildXL. For example, this is useful for the case of linting, where depending on whether a build is a release build, linter errors determine build success. Defaults to false.

### childProcessesToBreakawayFromSandbox: PathAtom[]
Lists process names that are allowed to escape BuildXL sandbox, and therefore won't be monitored, nor their actions registered. This is an unsafe option. Please check [here](../Advanced-Features/Process-breakaway.md) for details.

### customScripts: (packageName: string, location: RelativePath) => File | Map<string, FileContent>
Allows to customize the available scripts for a given package. Check the details [here](js-custom-scripts.md).

### successExitCodes: number[]
Exit codes that are considered successful. Any exit code outside the provided list is considered a failed execution, and BuildXL will treat it as a build breaker. This configuration applies to all pips scheduled by the corresponding resolver.

### retryExitCodes: number[]
Exit codes that cause BuildXL to retry the pip. By default this value is empty. If an exit code is also in 'successExitCode', then the pip is not retried on exiting with that exit code. This configuration applies to all pips scheduled by the corresponding resolver. The maximum number of retries is bound by 'processRetries'.

### processRetries: number
Maximum number of retries for processes. A process returning an exit code specified in 'retryExitCodes' will be retried at most the specified number of times. By default this value is not specified, in which case the build global max retry configuration defines this value (configured via command line arguments with /processRetries:n). If the global configuration is not specified, the default is 0, which implies that no retries will be attempted.