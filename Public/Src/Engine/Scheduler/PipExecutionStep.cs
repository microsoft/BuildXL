// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Tracing;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Scheduler
{
    /// <summary>
    ///  Execution steps for pips
    /// </summary>
    /// <remarks>
    /// WARNING: SYNC WITH PipExecutionUtils.AsString
    /// The state diagram is below:
    /// </remarks>
    /* 
     * To edit this diagram, please use the following link: https://asciiflow.com/#/share/eJzdWM1um0AQfhVrzxyqVlVUX3rAqWIpSa34EFXissGjGHm9i5alshPl1keI6Hv0aPVp%2FCTF%2BCcQ9h%2BSQxGSd2Hnm%2Fnmm1kwj4jiJaAhzQkJEMFr4GiIHiO0itDwy6fPQYTW5ejj2YdyJGAlykmEBt7H9vlv%2B4wi2glxKjAXPeDoI5twFkOWBSFL18EtTwQEU8BklHAtaB1C6uO%2FOV8x9dPALosuoFcg8CRJZVbjSbjH1hErDLT3UO2q8EliIUHWlI06tD2nUZIJntzlAmYXOJsn9L68%2BAOyrloXm%2FMVxCXsNaOHtthl%2BPcvjVE4ZyyDW8YXwMdpfIqxYnhO8R2B2Vepvo159zLtgGSoFFvPuireNmq91Kr6vWavlrUutG1fPNQHPSVP50taMOEc4sWYxhyWQAUm00XVkqpi1PX7O7BoHU75li1UXH6jp4PO26Fh1dGqKPSyT1r5GMHuTWQW4ngOl4wtcunurbavZto9xZOjT95lPk7TY10dG93FdtvYy8zmpp41CWjJWOPFUldDnD0p0082PTtCdsvOW%2F056tAg3WvbPVqXbFrF6pw1KWozax6vQ45RW5ejDnc%2Flj6GrK012XJnVWzsGNqbqjw3Kj7Nm9g3Of3G2bLS1KOK%2FXpCx%2B2PPbeuqmgXhYz%2BBH4PNAZ7TR3Y%2B%2BasGcEVFsATTJIHGNM0F4pePP3hULy8eCvZMyOVkC56Ku7bCKVarjYyeZ2wTLzkXHkWmwtMZwRuIMuJ8VOMY37tFXH%2FAHSJH9Y1P44Ater1AxgMvudiV%2Fa%2BERzN6uP3gfDptKKurJd828aWcUietCBHjMLbMXk2MEFP6Okf63yEwQ%3D%3D
                                                          ┌─────┐
                                                          │Start│
                                                          └──┬──┘
         Process,Copy,Write,SealDir                          │
         ┌───────────────────────────────────────────────────┼────────────────────┐
         │                                                   │                    │
         │                                            MetaPip│                 IPC│
┌────────▼─────────┐      Copy,Write,SealDir┌────────────────▼───┐        ┌───────▼───────┐
│DistributedHashing│Yes──────────┬──────────►ExecuteNonProcessPip◄────────┤ChooseWorkerIpc│
│     Enabled?     │             │          └─────────┬──────────┘        └───────────────┘
└──────────────────┘             │                    │
        Yes     No               No                   │
         │      │      ┌────────────────────┐         │
         │      └──────►CheckIncrementalSkip│Yes──────┤
         │             └────────────────────┘         │
         │                       No                   │
         │                       │                    │
         ├───────────────────────┘                    │
  Process│                                            │
┌────────▼─────────┐                                  │
│DelayedCacheLookup│                                  │
│     Enabled?     │                                  │
└──────────────────┘                                  │
         No      Yes                                  │
         │        │                                   │
         │      ┌─▼────────────────┐                  │
         │      │DelayedCacheLookup│                  │
         │      └─┬────────────────┘                  │
         │        │                                   │
┌────────▼────────▼─────┐                             │
│ChooseWorkerCacheLookup│                             │
└─────┬─────────────────┘                             │
      │                                               │
┌─────▼─────┐                                         │
│CacheLookup│Yes───────────────┐                      │
└───────────┘                  │                      │
      No                       │                      │
      │                        │                      │
┌─────▼─────────┐        ┌─────▼──────┐               │
│ChooseWorkerCpu│        │RunFromCache├───────────────┤
└─────┬─────────┘        └─────▲──────┘               │
      │                        │                      │
      │                    Convergence                │
┌─────▼───────────┐      ┌──────────────┐             │
│MaterializeInputs├──────►ExecuteProcess│             │
└─────────────────┘      └─────┬────────┘             │
                               │                      │
                          ┌────▼──────┐        ┌──────▼─────┐
                          │PostProcess├────────►HandleResult│
                          └───────────┘        └───────────┬┘
                                               Lazy        │
                                            Materialize    │
                                              Outputs      │
                                                 │         │
                                                 │         │
                                ┌────────────────▼─┐     ┌─┴──┐
                                │MaterializeOutputs├─────►Done│
                                └──────────────────┘     └────┘

*/
    public enum PipExecutionStep
    {
        /// <summary>
        /// None as a default step
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        None,

        /// <summary>
        /// Start executing the pip (e.g., changing pip state, recording start time)
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        Start,

        /// <summary>
        /// Cancel pip
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        Cancel,

        /// <summary>
        /// Skip processing the pip. This may either be due to upstream failed dependencies or cache misses when the user
        /// requests a cacheonly build. Skipped pips are not failures and do not fail the build. If they are skipped due
        /// failed dependencies, the upstream pip is the one that will cause the build session to be a failure.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        Skip,

        /// <summary>
        /// Check whether this pip is skipped due to incremental scheduling, if so ensure outputs are hashed
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        CheckIncrementalSkip,

        /// <summary>
        /// Materialize pip inputs
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        MaterializeInputs,

        /// <summary>
        /// Materialize pip outputs
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        MaterializeOutputs,

        /// <summary>
        /// Execute non-process pip (sealdirectory, copyfile, writefile, meta, ipc)
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ExecuteNonProcessPip,

        /// <summary>
        /// Do cache lookup for process
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        CacheLookup,

        /// <summary>
        /// Run process from cache
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        RunFromCache,

        /// <summary>
        /// Execute process
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ExecuteProcess,

        /// <summary>
        /// Analyze pip violations and store two phase cache entry
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        PostProcess,

        /// <summary>
        /// Call OnPipCompleted
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        HandleResult,

        /// <summary>
        /// Choosing a worker
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ChooseWorkerCpu,

        /// <summary>
        /// Choosing a worker for cache lookup
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ChooseWorkerCacheLookup,

        /// <summary>
        /// Delayed cache lookup queue.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        DelayedCacheLookup,

        /// <summary>
        /// Choosing a worker for IPC pips
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ChooseWorkerIpc,

        /// <summary>
        /// Done executing pip
        /// </summary>
        /// <remarks>
        /// WARNING: Done must be the last member of the enum.
        /// </remarks>
        [CounterType(CounterType.Stopwatch)]
        Done,
    }

    /// <summary>
    /// Utils for <see cref="PipExecutionStep"/>
    /// </summary>
    public static class PipExecutionUtils
    {
        /// <summary>
        /// Indicates if the pip execution step is tracked for the pip running time displayed in critical path printout
        /// </summary>
        public static bool IncludeInRunningTime(this PipExecutionStep step, IPipExecutionEnvironment environment)
        {
            switch (step)
            {
                // These steps pertain to distribution and thus should not be considered for running time
                // which is expected to be comparable whether the pip runs remotely or not
                case PipExecutionStep.DelayedCacheLookup:
                case PipExecutionStep.ChooseWorkerCpu:
                case PipExecutionStep.ChooseWorkerCacheLookup:
                case PipExecutionStep.ChooseWorkerIpc:
                    return false;
                case PipExecutionStep.MaterializeOutputs:
                    // If we materialize outputs in background, then do not include the duration in running time.
                    return !environment.MaterializeOutputsInBackground;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Indicates if the pip execution step is tracked for the pip running time displayed in critical path printout
        /// </summary>
        public static bool IncludeInTracer(this PipExecutionStep step)
        {
            switch (step)
            {
                case PipExecutionStep.ExecuteNonProcessPip:
                case PipExecutionStep.ExecuteProcess:
                case PipExecutionStep.MaterializeInputs:
                case PipExecutionStep.CacheLookup:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Indicates if the pip execution step is mainly I/O related.
        /// </summary>
        public static bool IsIORelated(this PipExecutionStep step)
        {
            switch (step)
            {
                case PipExecutionStep.Start: // Hashing
                case PipExecutionStep.CacheLookup:
                case PipExecutionStep.MaterializeInputs:
                case PipExecutionStep.MaterializeOutputs:
                case PipExecutionStep.PostProcess:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Indicates if the pip execution step is chooseworker related.
        /// </summary>
        public static bool IsChooseWorker(this PipExecutionStep step)
        {
            switch (step)
            {
                case PipExecutionStep.ChooseWorkerIpc:
                case PipExecutionStep.ChooseWorkerCpu:
                case PipExecutionStep.ChooseWorkerCacheLookup:
                    return true;
                default:
                    return false;
            }
        }


        /// <summary>
        /// Indicates if the pip execution step can run on the workers
        /// </summary>
        public static bool CanWorkerExecute(this PipExecutionStep step)
        {
            switch (step)
            {
                case PipExecutionStep.CacheLookup:
                case PipExecutionStep.ExecuteNonProcessPip:
                case PipExecutionStep.MaterializeInputs:
                case PipExecutionStep.ExecuteProcess:
                case PipExecutionStep.PostProcess:
                case PipExecutionStep.MaterializeOutputs:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Check whether it is valid to transition from one step to another pip execution step.
        /// </summary>
        public static bool CanTransitionTo(this PipExecutionStep fromStep, PipExecutionStep toStep)
        {
            if (toStep == PipExecutionStep.Cancel)
            {
                // You can transition to Cancel step from any step except None, Start, Done
                return fromStep != PipExecutionStep.None || fromStep != PipExecutionStep.Start || fromStep != PipExecutionStep.Done;
            }

            switch (fromStep)
            {
                case PipExecutionStep.None:
                    return toStep == PipExecutionStep.Start;

                case PipExecutionStep.Start:
                    return toStep == PipExecutionStep.Skip
                        || toStep == PipExecutionStep.CheckIncrementalSkip
                        || toStep == PipExecutionStep.ExecuteNonProcessPip
                        || toStep == PipExecutionStep.ChooseWorkerCpu
                        || toStep == PipExecutionStep.ChooseWorkerCacheLookup
                        || toStep == PipExecutionStep.DelayedCacheLookup
                        || toStep == PipExecutionStep.ChooseWorkerIpc
                        || toStep == PipExecutionStep.HandleResult;

                case PipExecutionStep.CheckIncrementalSkip:
                    return toStep == PipExecutionStep.ExecuteNonProcessPip
                        || toStep == PipExecutionStep.DelayedCacheLookup
                        || toStep == PipExecutionStep.ChooseWorkerCacheLookup
                        || toStep == PipExecutionStep.HandleResult;

                case PipExecutionStep.DelayedCacheLookup:
                    return toStep == PipExecutionStep.ChooseWorkerCacheLookup;

                case PipExecutionStep.ChooseWorkerCacheLookup:
                    return toStep == PipExecutionStep.CacheLookup
                        || toStep == PipExecutionStep.ChooseWorkerCacheLookup;

                case PipExecutionStep.CacheLookup:
                    return toStep == PipExecutionStep.RunFromCache
                         || toStep == PipExecutionStep.ChooseWorkerCpu
                         || toStep == PipExecutionStep.ChooseWorkerCacheLookup
                         || toStep == PipExecutionStep.HandleResult
                         || toStep == PipExecutionStep.Skip;

                case PipExecutionStep.ChooseWorkerIpc:
                    return toStep == PipExecutionStep.ExecuteNonProcessPip
                        || toStep == PipExecutionStep.ChooseWorkerIpc;

                case PipExecutionStep.ChooseWorkerCpu:
                    return toStep == PipExecutionStep.MaterializeInputs
                        || toStep == PipExecutionStep.ExecuteProcess
                        || toStep == PipExecutionStep.ChooseWorkerCpu;

                case PipExecutionStep.ExecuteProcess:
                    return toStep == PipExecutionStep.PostProcess
                        || toStep == PipExecutionStep.ChooseWorkerCpu /* retry */
                        || toStep == PipExecutionStep.RunFromCache    /* determinism probe - deploy outputs from cache after executing process to enable downstream determinism */
                        || toStep == PipExecutionStep.HandleResult;   /* failure */

                case PipExecutionStep.ExecuteNonProcessPip:
                case PipExecutionStep.PostProcess:
                    return toStep == PipExecutionStep.MaterializeOutputs /* lazy materialization off */
                        || toStep == PipExecutionStep.HandleResult       /* lazy materialization on */
                        || toStep == PipExecutionStep.ChooseWorkerCpu    /* retry */
                        || toStep == PipExecutionStep.ChooseWorkerIpc;   /* retry */

                // May need to materialize outputs due to cache convergence
                case PipExecutionStep.RunFromCache:
                    return toStep == PipExecutionStep.MaterializeOutputs /* lazy materialization off */
                        || toStep == PipExecutionStep.HandleResult;      /* lazy materialization on */

                case PipExecutionStep.MaterializeInputs:
                    return toStep == PipExecutionStep.ExecuteProcess
                        || toStep == PipExecutionStep.ChooseWorkerCpu       /* retry */
                        || toStep == PipExecutionStep.HandleResult;         /* failure */

                case PipExecutionStep.Cancel:
                case PipExecutionStep.Skip:
                    return toStep == PipExecutionStep.HandleResult;

                case PipExecutionStep.MaterializeOutputs:
                    return toStep == PipExecutionStep.HandleResult
                        || toStep == PipExecutionStep.Done; /* background output materialization */

                case PipExecutionStep.HandleResult:
                    return toStep == PipExecutionStep.Done
                        || toStep == PipExecutionStep.MaterializeOutputs; /* background output materialization */

                case PipExecutionStep.Done:
                    return false;

                default:
                    throw Contract.AssertFailure("Invalid step:" + fromStep);
            }
        }

        /// <summary>
        /// Efficient toString implementation for PipExecutionStep.
        /// </summary>
        public static string AsString(this PipExecutionStep step)
        {
            switch (step)
            {
                case PipExecutionStep.CacheLookup:
                    return nameof(PipExecutionStep.CacheLookup);
                case PipExecutionStep.Cancel:
                    return nameof(PipExecutionStep.Cancel);
                case PipExecutionStep.CheckIncrementalSkip:
                    return nameof(PipExecutionStep.CheckIncrementalSkip);
                case PipExecutionStep.ChooseWorkerCacheLookup:
                    return nameof(PipExecutionStep.ChooseWorkerCacheLookup);
                case PipExecutionStep.ChooseWorkerIpc:
                    return nameof(PipExecutionStep.ChooseWorkerIpc);
                case PipExecutionStep.ChooseWorkerCpu:
                    return nameof(PipExecutionStep.ChooseWorkerCpu);
                case PipExecutionStep.Done:
                    return nameof(PipExecutionStep.Done);
                case PipExecutionStep.ExecuteNonProcessPip:
                    return nameof(PipExecutionStep.ExecuteNonProcessPip);
                case PipExecutionStep.ExecuteProcess:
                    return nameof(PipExecutionStep.ExecuteProcess);
                case PipExecutionStep.HandleResult:
                    return nameof(PipExecutionStep.HandleResult);
                case PipExecutionStep.MaterializeInputs:
                    return nameof(PipExecutionStep.MaterializeInputs);
                case PipExecutionStep.MaterializeOutputs:
                    return nameof(PipExecutionStep.MaterializeOutputs);
                case PipExecutionStep.None:
                    return nameof(PipExecutionStep.None);
                case PipExecutionStep.PostProcess:
                    return nameof(PipExecutionStep.PostProcess);
                case PipExecutionStep.RunFromCache:
                    return nameof(PipExecutionStep.RunFromCache);
                case PipExecutionStep.Skip:
                    return nameof(PipExecutionStep.Skip);
                case PipExecutionStep.Start:
                    return nameof(PipExecutionStep.Start);
                case PipExecutionStep.DelayedCacheLookup:
                    return nameof(PipExecutionStep.DelayedCacheLookup);
                default:
                    throw new NotImplementedException("Unknown PipExecutionStep type: " + step);
            }
        }
    }
}
