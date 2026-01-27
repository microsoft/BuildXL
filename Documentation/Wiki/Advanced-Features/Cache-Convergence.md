## What is Cache Convergence?

Cache convergence is a behavior where BuildXL allows a Process Pip to "snap to" or adopt the results from another concurrent build that completed the same pip first. This feature is particularly useful when dealing with non-deterministic build tools.

### How It Works

When two builds (Build A and Build B) are racing to build the same pip:

1. Build A completes the pip execution and stores its results to the cache
2. Build B completes the pip execution and attempts to store its results to the cache. Consider the pip may be non-deterministic, and its outputs different from A's execution
3. The cache indicates that a pre-existing entry already exists (from Build A)
4. Build B chooses to converge to Build A's cached result instead of using its own execution result
5. This allows Build B to rejoin cache hit sharing with Build A for downstream pips

This behavior acknowledges that most tools have some degree of non-determinism, and converging gives builds a chance to re-establish cache hit sharing after executing pips that might have produced slightly different outputs.

### Why Is This Useful?

Non-deterministic tools may produce outputs that differ slightly between executions even with identical inputs. Without convergence:

- Each build would generate its own unique cache entries
- Downstream pips would see different inputs, causing cascading cache misses
- Parallel builds would increasingly diverge in their cache hit rates

With convergence:

- Builds can rejoin and share cache hits after pip races
- Reduced cache pollution from near-identical outputs
- Better overall cache hit rates across concurrent builds

## Identifying Convergence in Logs

When convergence occurs, BuildXL logs a verbose message in the build log:

```
verbose DX2728: [{pipDescription}] While trying to store a cache entry for this pip's execution, the cache indicated that a conflicting entry already exists (strong fingerprint: {strongFingerprint}). This may occur if a concurrent build is storing entries to the cache and won the race of placing the content
```

This message indicates that:
- The current build executed a pip
- Another build had already stored a result for the same pip (same strong fingerprint)
- The current build may converge to use the existing cached result

## Delayed Cache Lookup

BuildXL includes a feature to delay cache lookups to reduce the likelihood of scenarios where concurrent builds duplicatively execute the same pip with the same inputs.

### The Problem

Originally, BuildXL would perform cache lookups as soon as a pip's dependencies were satisfied. However, this created a timing issue:

1. Cache lookups are relatively cheap compared to executing pips
2. BuildXL would perform many cache lookups quickly
3. The scheduler might not have resources to execute cache-miss pips for tens of minutes
4. During this lag time, concurrent builds could execute and cache the same pips
5. When the original build finally executes its cache-miss pip, it's redundantly running work that's now in the cache

This large lag time between lookup and execution increased the probability of redundant pip executions across concurrent builds.

### The Solution

BuildXL throttles how far ahead in the build graph it performs cache lookups, keeping the window between lookup and potential execution tight. This reduces the chance that another build will execute and cache a pip between this build's lookup and execution.

### Configuration Parameters

In general, the delayed cache lookup settings shouldn't need to be configured. BuildXL performs cache lookups at a rate to ensure it workers don't run out of workload to run while still throttling cache lookups so they don't get too far ahead of exectuion. This is achieved by having a target for the number of pips in the queue that corresponds waiting to be scheduled on a worker. The larger that queue is allowed the grow, the more aggressively pips perform cache lookups ahead of time and also the longer the gap between cache lookup and subsequent execution when a pip is a cache miss. BuildXL calculates the target depth of this queue using the sum of the number of pip execution 'slots' (max concurrency) across all machines and a multiplier which can be overridded using the options below:

#### `/delayCacheLookupMin:<value>`

Specifies the minimum multiplier for delaying cache lookups. Value must be between 0 and 100. Defaults to 1.

#### `/delayCacheLookupMax:<value>`

Specifies the maximum multiplier for delaying cache lookups. Value must be between 0 and 100. Defaults to 2.

**Important Notes:**
- Both parameters must be specified together
- `/delayCacheLookupMin` must be less than or equal to `/delayCacheLookupMax`
- These parameters control how aggressively BuildXL delays cache lookups relative to available execution resources

### Example Usage

```bash
bxl /delayCacheLookupMin:1.0 /delayCacheLookupMax:2.5
```

## Performance Considerations

### Measuring Convergence Impact

You can analyze convergence patterns in your builds by examining datapoints in the .stats log file

1. **ProcessPipTwoPhaseCacheEntriesConverged**: Number of pips in a build that converged with pre-existing cache entries
2. **ExecuteConvergedProcessDurationMs**: Total execution time spent executing pips that ultimately converged with the cache, throwing away the results of the current build's execution. Note this counter will be the sum for pips that execute concurrently, not wall clock time.
3. **ExecuteProcessDurationMs**: Useful to compare against ExecuteConvergedProcessDurationMs

### When to Tune These Settings

Consider adjusting delayed cache lookup settings when:

- You have many concurrent builds of the same project
- You observe high convergence rates (>25% of total execution time)
- There's significant lag between cache lookup and pip execution

However, for most scenarios, the default settings work well and tuning is not necessary.