## BuildXL Process Timeouts
Process pips launched by BuildXL have configurable timeouts.
The overall timeout will cause a pip to be terminated and considered a failure if exceeded.
There is also a warning timeout that will be printed as a hint to users, to indicate that they are reaching a time limit.

There are a few ways the timeouts are set:

* A default timeout that applies to all pips. This defaults to 10 minutes if not specified, but can be overridden on the command line.

  * `/pipDefaultTimeout:<ms>` - How long to wait before terminating individual processes, in milliseconds. Setting this value will only have an effect if no other timeout is specified for a process.

  * `/pipDefaultWarningTimeout:<ms>` - After how much time to issue a warning that an individual process is running too long, in milliseconds. Setting this value will only have an effect if no other timeout is specified for a process; see command line help text for more details.

* Per-process timeouts are configurable at graph construction time. These override the global timeout.
   ```ts
   /**
     * Provides a hard timeout after which the Process will be marked as failure due to timeout and terminated.
     */
   timeoutInMilliseconds?: number;

   /**
    * Sets an interval value that indicates after which time BuildXL will issue a warning that the process is running longer
    * than anticipated
    */
   warningTimeoutInMilliseconds?: number;
   ```
   (see the `Transformer.execute` documentation for more details)

* A timeout multiplier on the commandline, which defaults to 1. If set, the timeout from the rules above will be multiplied by this value to get the effective timeout.

  * `/pipTimeoutMultiplier:<float>` - Multiplier applied to the final timeout for individual processes. Setting a multiplier greater than one will increase the timeout accordingly for all pips, even those with an explicit non-default timeout set.
  * `/pipWarningTimeoutMultiplier:<float>` - Multiplier applied to the warning timeout for individual processes. Setting a multiplier greater than one will increase the warning timeout accordingly for all pips, even those with an explicit non-default warning timeout set (see command line help text).

The following happens when the timeout is reached:
* The job object for the process is enumerated and a heap dump is taken for all currently running processes in the tree.
* The job object is killed, terminating all processes
* The process pip is marked as a failure, preventing all downstream pips from being run.

To aid in discovering when pips are at the brink of the failure threshold, there is a second warning threshold that can be configured at a global and per-pip level similar to what is described above.

## Other Timeouts
There is also a "process injection" timeout. This is the amount of time BuildXL allows for spawning a process and the process sandboxing injecting itself into the running process. Generally this happens on the order of milliseconds, but on a very heavily loaded computer it may take much longer. The timeout for this is set to 10 minutes but this timeout is not user configurable.
