#  Resource usage configuration

BuildXL has a few options to configure how many resources your build consumes on your machine. By default it attempts to perform the build as fast as possible. But this can cause other tasks on the machine to become unresponsive. There may also be some machine configurations for which the default settings need to be tuned to achieve the desired result.

## Process Concurrency Limit
The most powerful setting to modify is the maximum number of child process pips BuildXL will launch at a time. This can be configured with one of two options:

    /maxProc:<number of concurrent processes>
                                Specifies the maximum number of processes that BuildXL will launch at one time. The
                                default value is 25% more than the total number of processors in the current machine.
    /maxProcMultiplier:<double> 
                                Specifies maxProc in terms of a multiplier of the machine's processor count. The
                                default is 1.25.

**Note** - In WDG builds, db.exe automatically sets /maxProcMultiplier to 1.5. So that's the effective default if you are in wdg

## Keeping computer responsive while building
By default, BuildXL slightly oversaturates the computer as that generally yields the best build time. If you find your machine unresponsive while performing other tasks, the recommendation is to decrease the number to remove the oversaturation. So if your computer has 8 physical cores, you may want to build with /maxProc:6 in order to leave CPU cycles for other processes while building.


Your computer may also be unresponsive due to hard drive contention. Reducing the number of concurrent build processes is a way of keeping your disk responsive for other applications as well.

## IO Concurrency limit 
Some pips are primitives for IO operations. These are:
* HashSourceFile pips
* WriteFile pips
* CopyFile pips

Since these pips are IO intensive and generally not CPU intensive, they have a different concurrency limit settings

    /maxIO:<number of concurrent I/O operations>
                                Specifies the maximum number of I/O operations that BuildXL will launch at one time. The
                                default value is 1/4 of the number of processors in the current machine, but at least
                                1.
    /maxIOMultiplier:<double>   
                                Specifies maxIO in terms of a multiplier of the machine's processor count. The default
                                is 0.25.

## Low priority
Processes in Windows use the "Normal" priority by default but can be overridden with another setting. This can be manipulated in Task Manager but BuildXL accepts it as a command line option as well. Generally this is less effective than setting the concurrency limit directly.

    /lowPriority[+|-]           Runs the build engine and all tools at a lower priority in order to provide better
                                responsiveness to interactive processes on the current machine.

## Memory utilization ##
BuildXL supports throttling spawned process pips based on machine available RAM. This aims at ensuring builds don't page memory onto disk for machines with low memory to CPU core ratios.

    /maxRamUtilizationPercentage:<number>
                                Specifies the maximum machine wide RAM utilization allowed before the scheduler will
                                stop scheduling more work to allow resources to be freed. Default is 85%.
    /minAvailableRamMb:<number> 
                                Specifies the minimum available machine wide RAM (in megabytes) allowed before the
                                scheduler will stop scheduling more work to allow resources to be freed. Default is 500
                                mb.

By default, BuildXL will actively kill processes when the maximum memory utilization is exceeded

    /disableProcessRetryOnResourceExhaustion[+|-]
                                Specifies that BuildXL should not kill processes when the limits are exceeded. 
                                Default to false (process killing and retrying is active)
