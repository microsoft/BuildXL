# Dynamic Graph Working Notes

Dynamic graph mode is an experimental execution mode enabled by
`unsafe_EnableDynamicGraph`. It allows pips to be admitted and executed while
graph-producing frontends are still publishing the graph. These restrictions
are intentionally collected here, including restrictions that cannot yet be
validated from configuration.

## Upfront restrictions/degraded features

Not exhaustive/definitive. This is just what looks initially more challenging to keep.

### Enforced configuration restrictions

- Distribution is rejected because workers receive a finalized graph snapshot
  before execution begins.
- Incremental scheduling is rejected because dirty-node computation and
  propagation currently require the complete graph.
- Pip filtering is rejected because selection, especially negation and
  dependent closure, requires the complete pip universe.
- Scrubbing is rejected because it needs the complete set of declared paths
  before deciding which filesystem entries are extraneous.
- Explicit graph loading is rejected because it supplies a finalized immutable
  graph instead of a live graph publication session.
- Graph caching is disabled because the current serialization pipeline assumes
  graph construction finishes before execution begins.

### Restrictions for dynamic pip admission

- A pip may depend only on pips and artifacts that were committed before it (so,
  same restriction as the regular pip graph building process).
- Service-related pips are unsupported in the initial dynamic mode. This
  includes service, client, shutdown, and finalization pips. Standalone IPC pips
  are not inherently incompatible.
- Shared opaque directories are unsupported until their graph-wide cleanup
  semantics are redesigned.
- Graph-conformance validation must be performed incrementally whenever a pip
  is admitted. This includes dependency ordering, artifact producers, path
  ownership and overlap, directory seals, and pip-specific rules.
- Late output-directory existence assertions are unsupported.
- Pips may not add new pips for now. Existing frontends can publish through the
  shared mutable pip graph; executing pips are not graph producers yet.
- While graph publication is open, graph-producing frontends and tools must not
  read paths that admitted pips may modify, unless that interaction is
  represented by an explicit publication barrier.

### Initially degraded or disabled scheduler functionality

- Exact downstream critical-path prioritization and end-of-build critical-path
  reporting are disabled because sinks and dependent closures are incomplete
  while publication is open. Dynamic scheduling uses explicit pip priority.
- Module affinity initialization is disabled because it builds the complete
  module-to-worker mapping from the final selected pip set. Note: we might be able
  to keep this since even under dynamic graph, all the available modules should still be known upfront.
- Process-remoting static-directory registration is disabled because it scans
  the complete selected pip set before execution to register every directory.
- The scheduler simulator is disabled because it analyzes the completed static
  schedule and consumes a concrete `PipGraph`.
- Binary execution logging is disabled because its header requires the final
  graph ID and maximum serialized path index before pip events are written.
- Packed execution export is disabled because it creates graph-wide pip, path,
  and dependency tables before consuming execution events.
- Failed-pip dump logging is disabled because `DumpPipLiteExecutionLogTarget`
  consumes the finalized graph.
- Observed-input anomaly analysis is disabled because
  `ObservedInputAnomalyAnalyzer` consumes the finalized graph.
- Filtering and build-set calculation are disabled because they require the
  complete pip universe and, for dependent selection, final outgoing closures.
- Incremental scheduling state creation, loading, and dirty propagation are
  disabled because they require final graph identity and dependency closure.
- Service lifecycle management and its statistics are disabled because service
  and service-related pips are unsupported in the initial mode.
- Shared-opaque sideband examination and lazy shared-opaque deletion are
  unavailable because shared opaque directories are unsupported.
- Distribution initialization and worker execution-log forwarding are disabled
  because workers currently receive final graph identity, path serialization
  bounds, and a complete graph payload before execution.
- Locally persisted historic performance data remains usable by pip semi-stable
  hash. Remote retrieval of the containing table is unavailable to early pips
  because its cache key includes the final graph fingerprint.
- Status and progress totals are degraded because `PipTable.Count` is only the
  number admitted so far, not the final number of pips.

### Satellite functionality deferred until graph closure

These features do not need to participate in dynamic scheduling, but they must
use the immutable graph produced after publication completes:

- Engine-state creation and reuse require the final graph ID and retain the
  immutable `PipGraph`.
- Graph and execution-state serialization require the finalized graph and run
  only after graph publication closes.
- IDE generation consumes the complete graph and is deferred until finalization.
- Clean-only output selection requires the complete filtered output set and is
  deferred until finalization.
- Scrubbing and module scrub-directory discovery require complete module,
  output-directory, temporary-path, and seal-directory sets. Scrubbing remains
  rejected in dynamic mode rather than merely deferred.
- Remote historic metadata and running-time table persistence require the final
  graph semi-stable fingerprint for their cache keys.
- Execution-result path serialization currently captures a maximum path index.
  This remains relevant only to disabled distribution; supporting it dynamically
  would require a versioned or growable path serialization protocol.

### Non-obvious dynamic dependencies

- Parent completion currently scans outgoing edges once. A child admitted after
  its parent completes will miss the completion notification.
- Child admission currently initializes its reference count from all incoming
  edges. It must instead register with an unfinished parent or observe and
  immediately apply an already-terminal parent result. This registration and
  completion transition must be atomic with respect to each other.
- `FileSystemView.Create` pre-populates a cache using the final artifact count.
  Skipping pre-population is easy, but cached negative path-existence results
  must be invalidated when a later pip declares an artifact under that path.
- API server creation checks the graph moniker once during scheduler startup.
  A moniker requested later requires a metadata notification or an upfront
  publication barrier.
- Service initialization normally snapshots all service and finalization
  relationships. Since services are initially unsupported, the exercise uses
  the no-service manager instead of adapting that snapshot.
- Status and progress reporting read `PipTable.Count` as a total. During graph
  publication this is only the number of pips admitted so far and cannot be
  used as a stable progress denominator.
- Outgoing-edge, consumer, and dependent queries are snapshots of what has been
  admitted so far. Callers must not interpret an empty result as final until
  graph publication closes.
- A late child initializes its dependency count from parents that are not yet
  terminal and immediately inherits failure from parents already observed as
  failed. This handles the deterministic parent-completed-before-admission case;
  atomically racing parent completion against child admission still requires a
  production registration protocol.
- `EngineSchedule` now distinguishes the scheduler's live `IDynamicGraph` from
  its closure-time `FinalizedPipGraph`. Static creation supplies both at once;
  dynamic creation attaches the immutable graph after successful builder
  completion.
- Dynamic mode initializes the scheduler and starts its dispatcher immediately
  after creating `EngineSchedule` from the builder. The admission consumer and
  already-published pips can therefore run while `PopulateGraph` continues.
  The normal execute phase observes that the scheduler is already started and
  waits for completion instead of starting it a second time.
- Graph-construction input tracking is disabled with graph caching. A frontend
  can observe an executing pip's output transition from absent to present,
  which is invalid under the input tracker's immutable-input model. A production
  protocol needs an explicit barrier or a separate class of execution signals.
- `EngineState` and graph serialization remain closure-time operations and use
  `EngineSchedule.FinalizedPipGraph`. They must not capture the live graph.
- `MaxSerializedAbsolutePath` snapshots the current path table only to keep the
  engine wrapper compiling. It is used by distribution, which remains disabled;
  dynamic distribution would need versioned path serialization.
