# Distributed builds
This document describes the high level design and the components of distributed builds in BuildXL.

## Components and terms
- **Orchestrator** – this machine initiates the build and is responsible for constructing the initial schedule, connects to workers, and orchestrating the builds.
- **Worker** - a machine that receives requests to execute certain pip steps and reports the result back to the orchestrator
- **Cache** - the [cache](../../Public/Src/Cache/README.md) is used to exchange files between the orchestrator and the worker (e.g. files for reconstructing the pip graph, input files for execution)    
- [**Worker**](../../Public/Src/Engine/Scheduler/Distribution/Worker.cs) object -  A local or remote worker capable of executing processes and IPC pips. The scheduler keeps a list of Workers (wich always has a single local worker, and additionally the remote workers when running distributed builds). 
- [**PipExecutionStep**](../../Public/Src/Engine/Scheduler/PipExecutionStep.cs) - A specific step in a pip's execution. Some of these steps can be distributed and executed in a remote worker.
- [**OrchestratorService**](../../Public/Src/Engine/Dll/Distribution/OrchestratorService.cs) - This service runs only in the orchestrator and is in charge of keeping track of the remote workers and receiving their messages (i.e., attachment and pip execution completions, and error events). `OrchestratorService` does not directly _send_ messages to the workers: this is done mainly by the scheduler itself through a `RemoteWorker` instance. 
- [**WorkerService**](../../Public/Src/Engine/Dll/Distribution/WorkerService.cs) - This service runs only in the workers and is in charge of communicating with the orchestrator, both receiving and sending messages (for orchestrator attachment, pip step execution requests/results and warning/error events).
- [**RemoteWorker**](../../Public/Src/Engine/Dll/Distribution/RemoteWorker.cs) -  A subclass of `Worker` capable of executing processes on external machines. These objects live in the orchestrator (in the scheduler's worker list and in the `OrchestratorService`) and are ultimately the ones that issue the different messages that will go through gRPC to the corresponding remote `WorkerService`s.

## Remote communication
The communication between the orchestrator and workers is carried out through remote procedure calls using [gRPC](https://grpc.io/). The RPC endpoints are implemented in:

- [**GrpcOrchestratorClient**](../../Public/Src/Engine/Dll/Distribution/Grpc/GrpcOrchestratorClient.cs) - Held by workers to send messages to the orchestrator
- [**GrpcWorkerClient**](../../Public/Src/Engine/Dll/Distribution/Grpc/GrpcWorkerClient.cs) - Held by the orchestrator to send messages to a worker 
- [**GrpcWorkerServer**](../../Public/Src/Engine/Dll/Distribution/Grpc/GrpcWorkerServer.cs) - Receives messages on the worker and calls `WorkerService` methods appropriately 
- [**GrpcWorkerServer**](../../Public/Src/Engine/Dll/Distribution/Grpc/GrpcOrchestratorServer.cs) - Receives messages on the orchestrator and calls `OrchestratorService` methods appropriately 
- [**ClientConnectionManager**](../../Public/Src/Engine/Dll/Distribution/Grpc/ClientConnectionManager.cs) - Manages the gRPC channels, monitoring the connection status and logging events


## Initialization and attachment
The orchestrator and the workers run the same BuildXL executable: the `distributedBuildRole` command line parameter indicates whether the invocation corresponds to a orchestrator or a worker. When the BuildXL engine is initialized, this [configuration](../../Public/Src/Utilities/Configuration/IDistributionConfiguration.cs) is checked and the different behavior is adopted.

### Workers
The workers don't initially know how to communicate with the orchestrator: they just block waiting for an _Attach_ request. With the attach request the worker receives both the address of the orchestrator (to set up its own RPC channel) and hashes for all the files needed to construct the pip graph for the build (in the form of a [`PipGraphCachedDescriptor`](../../Public/Src/Engine/Cache/Fingerprints/PipGraphCacheDescriptor.cs)). This lets the worker construct the build graph (after pulling those files from the cache) for the session.

After setting up the cache and building the graph the worker pushes a validation content to the cache (which the orchestrator will try to retrieve to validate the cache connection is working properly: see below), notifies the orchestrator that the attachment is completed, and starts waiting for execution requests. 

### Orchestrator
The orchestrator starts with a list of the addresses for all the workers and sends an attachment request to each one of them as described above. The `RemoteWorker`s are initialized by the orchestrator's scheduler and transition through different steps throughout the process:

- `NotStarted` -> `Starting`: Before it sends the attachment request 
- `Starting` -> `Started`: After successfully sending the Attach RPC
- `Started` -> `Attached`: After receiving the attach completion RPC from the worker (as described above)
- `Attached` -> `Running`: After validating that it can succesfully pull from the cache the validation content pushed by the worker. This checks that we can communicate content between orchestrator and worker through the cache (as the worker successfully retrieved the pip graph and we succesfully retrieved this validation content).

After transitioning to the `Running` state the `RemoteWorker` starts the thread that will send requests to the remote worker.

## Execution

- We only distribute **IPC pips** and **process pips**. For these kinds of pips, the `CacheLookup`, `MaterializeInputs`, `ExecuteProcess` and `PostProcess` [execution steps](../../Public/Src/Engine/Scheduler/PipExecutionStep.cs) can be distributed. 

- A pip build request sent to a worker (see [RemoteWorker.SendBuildRequest](../../Public/Src/Engine/Dll/Distribution/RemoteWorker.cs#L141)) consist essentially of a pip id, the step to execute, and the hashes of the inputs for that pip. The hashes are needed for fingerprinting in the cache lookup step and for downloading the files before execution in an actual execution step.

- Because fulfilling the requests can take a long time (i.e. a pip execution can take hours) the RPC just represents a build request and the acknowledgment from the worker. The orchestrator creates a `PipCompletionTask` associated to the request, which will complete when in the future we get a message from the worker after it completes the step (see the examples below). 

- Both the build requests (from the orchestrator to a worker) and the build results (from worker to orchestrator) are queued and sent in batches to the corresponding endpoint. 

- The [binary execution log](How-To-Run-BuildXL/Log-Files/BuildXL.xlg.md) is constantly funneled (alongside the pip results) from every worker to the orchestrator, which coalesces the information into the single execution log for the build. 

- The scheduler running in a worker doesn't do any actual "scheduling" besides managing concurrency and resource utilization. It just receives the specific steps to queue for execution from the WorkerService.

- The `MaterializeInputs`, `ExecuteProcess` and `PostProcess` steps must happen on the same worker for the same pip to ensure cache convergence. 

## Error handling and retry
### Network failure
A thread in `ClientConnectionManager` monitors changes in the gRPC [channel state](https://github.com/grpc/grpc/blob/master/doc/connectivity-semantics-and-api.md) and tries to reconnect if it notices the connection was lost. From the orchestrator, if the reconnection attempts fail, the worker will be transitioned to the `Stopped` state. Any pending pip that was assigned to the lost worker can be retried a number of times (controlled by the `/numRetryFailedPipsOnAnotherWorker` configuration).

### Forwarded events
The worker also forwards error and warning events to the orchestrator through the `Notify` RPC. When receiving an error event, the orchestrator can decide to stop the worker and, depending on the type of error, fail the build or not (e.g., we don't fail the build because of infrastructure errors on the worker such as low disk space). 

## Examples
For every machine, the logs for the gRPC activity can be found in the `BuildXL.DistributionRpc.log` in the build log folder. Each remote calls is associated with a trace ID (in the form of a GUID). 

### Example activity: attachment
The following is the gRPC activity during the attachment process. Note that the `Attach` RPC is done (`Sent`) quickly but the actual process takes some time and completion is signaled by the `AttachCompleted` message from the worker.

**Logs from the orchestrator (`MW1APS1979A83D`)**:
```log
[1:52.464] Grpc: [SELF -> MW1APS19798679:89] 7ae8b153-acaf-40be-82f5-21c7dcfdd838 Call#1. Attach.
[1:52.467] Grpc: [SELF -> MW1APS19799544:89] 3411ab4c-e508-45d9-9267-6ab7b1394e94 Call#1. Attach.
[1:52.555] Grpc: [SELF -> MW1APS19798679:89] 7ae8b153-acaf-40be-82f5-21c7dcfdd838 Sent#1. Duration: 91ms.
[1:52.559] Grpc: [SELF -> MW1APS19799544:89] 3411ab4c-e508-45d9-9267-6ab7b1394e94 Sent#1. Duration: 92ms.
[2:02.486] Grpc: [MW1APS19798679 -> SELF] 18fe9e81-6af6-415c-9e7d-457c3bf1da17 Received: /BuildXL.Distribution.Grpc.Orchestrator/AttachCompleted.
[2:02.486] Grpc: [MW1APS19799544 -> SELF] 7945a7eb-c26b-41b5-bcd0-551c3ac412c5 Received: /BuildXL.Distribution.Grpc.Orchestrator/AttachCompleted.
[2:02.504] Grpc: [MW1APS19798679 -> SELF] 18fe9e81-6af6-415c-9e7d-457c3bf1da17 Responded: /BuildXL.Distribution.Grpc.Orchestrator/AttachCompleted. DurationMs: 17.
[2:02.504] Grpc: [MW1APS19799544 -> SELF] 7945a7eb-c26b-41b5-bcd0-551c3ac412c5 Responded: /BuildXL.Distribution.Grpc.Orchestrator/AttachCompleted. DurationMs: 17.
```

**Logs from a worker (`MW1APS19799544`)**:
```log
[1:51.709] Grpc: [MW1APS1979A83D -> SELF] 3411ab4c-e508-45d9-9267-6ab7b1394e94 Received: /BuildXL.Distribution.Grpc.Worker/Attach.
[1:51.727] Grpc: [MW1APS1979A83D -> SELF] 3411ab4c-e508-45d9-9267-6ab7b1394e94 Responded: /BuildXL.Distribution.Grpc.Worker/Attach. DurationMs: 16.
[2:01.619] Grpc: Attempt to connect to MW1APS1979A83D:89. ChannelState Idle. Operation AttachCompleted.
[2:01.622] Grpc: [MW1APS1979A83D:89] Channel state: Idle -> Connecting.
[2:01.624] Grpc: [MW1APS1979A83D:89] Channel state: Connecting -> Ready.
[2:01.624] Grpc: Connected to MW1APS1979A83D:89. ChannelState Ready. Duration 7ms.
[2:01.626] Grpc: [SELF -> MW1APS1979A83D:89] 7945a7eb-c26b-41b5-bcd0-551c3ac412c5 Call#1. AttachCompleted.
[2:01.686] Grpc: [SELF -> MW1APS1979A83D:89] 7945a7eb-c26b-41b5-bcd0-551c3ac412c5 Sent#1. Duration: 60ms.
```



### Example activity: execution
Shown here are a request for pip execution and some reporting back from the worker for previously requested pips. Note the gRPC logs provide only the pip ids sent, without information of the actual step requested, but that information can be found around the same timestamp in the `BuildXL.log`:

`BuildXL.log` messages in the orchestrator (for pips `DA3F6771D1546866` and `73C93B4BE473A03C`):
```log
[2:10.219] [PipDA3F6771D1546866] Requesting CacheLookup on #2 (MW1APS19799544::89)
(...)
[2:10.261] [Pip73C93B4BE473A03C] Finished CacheLookup on #2 (MW1APS19799544::89)
[2:10.261] [Pip73C93B4BE473A03C, ProcessRunner.exe] Cache hit (fingerprint '4021526F1660E5D8CB67F1B58BBCCEB27DE8DC1A'; unique ID 3D5CE3B96324981A): Process outputs will be deployed from cache.
```

**`BuildXL.DistributionRpc.log` from the orchestrator (`MW1APS1979A83D`)**:
```log
[2:09.933] Grpc: [SELF -> MW1APS19799544:89] 39e1801d-551a-4ec6-8fe6-0954e1f919c6 Call#1. ExecutePips: DA3F6771D1546866, 5C0DAD11AAC0D4BB, 56EE06CEF53E526C.
[2:09.936] Grpc: [SELF -> MW1APS19799544:89] 39e1801d-551a-4ec6-8fe6-0954e1f919c6 Sent#1. Duration: 3ms.
[2:09.969] Grpc: [MW1APS19799544 -> SELF] 02b41b4e-b55f-4f41-8585-0c701c5ea236 Received: /BuildXL.Distribution.Grpc.Orchestrator/Notify.
[2:09.971] Grpc: [MW1APS19799544 -> SELF] 02b41b4e-b55f-4f41-8585-0c701c5ea236 Responded: /BuildXL.Distribution.Grpc.Orchestrator/Notify. DurationMs: 1.
```

**`BuildXL.DistributionRpc.log` logs from the worker (`MW1APS19799544`)**:
```log
[2:09.108] Grpc: [MW1APS1979A83D -> SELF] 39e1801d-551a-4ec6-8fe6-0954e1f919c6 Received: /BuildXL.Distribution.Grpc.Worker/ExecutePips.
[2:09.109] Grpc: [MW1APS1979A83D -> SELF] 39e1801d-551a-4ec6-8fe6-0954e1f919c6 Responded: /BuildXL.Distribution.Grpc.Worker/ExecutePips. DurationMs: 0.
[2:09.129] Grpc: [SELF -> MW1APS1979A83D:89] 02b41b4e-b55f-4f41-8585-0c701c5ea236 Call#1. NotifyPipResults: 73C93B4BE473A03C, A002757A1FF615C3, D1683B1C9C4C7389
[2:09.146] Grpc: [SELF -> MW1APS1979A83D:89] 02b41b4e-b55f-4f41-8585-0c701c5ea236 Sent#1. Duration: 16ms.
```

### Example activity: forwarding error events
Note that the RPC to the orchestrator is the same as when reporting execution results (`Notify`):

**Worker**:
```log
[1:25:35.162] verbose DX7029: Grpc: [SELF -> MW1APS1979A83D:89] f37d5550-716d-41ae-b403-4b315309c16b Call#1. ForwardedEvents: Count=1.
[1:25:35.164] verbose DX7029: Grpc: [SELF -> MW1APS1979A83D:89] f37d5550-716d-41ae-b403-4b315309c16b Sent#1. Duration: 1ms.
```

**Orchestrator**:
```log
[1:25:35.989] verbose DX7029: Grpc: [MW1APS19799544 -> SELF] f37d5550-716d-41ae-b403-4b315309c16b Received: /BuildXL.Distribution.Grpc.Orchestrator/Notify.
[1:25:35.989] verbose DX7029: Grpc: [MW1APS19799544 -> SELF] f37d5550-716d-41ae-b403-4b315309c16b Responded: /BuildXL.Distribution.Grpc.Orchestrator/Notify. DurationMs: 0.
```

**Orchestrator's `BuildXL.log`**:

```log
[1:25:35.989] Worker #2 (MW1APS19799544::89) logged warning:
warning DX0015: [Pip9E3488D3A7B8CE8B] Process ran for 4676575ms, which is longer than the warning timeout of 510000ms; the process will be terminated if it ever runs longer than 964800000ms
```