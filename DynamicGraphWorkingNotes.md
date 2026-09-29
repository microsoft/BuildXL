# Dynamic Graph Working Notes

Dynamic graph mode is an experimental execution mode enabled by
`unsafe_EnableDynamicGraph`. It allows pips to be admitted and executed while
graph-producing frontends are still publishing the graph. This file records the
current restrictions and remaining design constraints. Implementation mechanics
that are already represented in code are omitted.

## Upfront restrictions/degraded features

This list is intentionally conservative and not exhaustive.

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
- `RealAndPipGraph` and `AlwaysMinimalWithAlienFilesGraph` filesystem modes are
  rejected because they require whole-graph or real-filesystem information that
  is not stable while graph publication remains open.

### Restrictions for dynamic pip admission

- A pip may depend only on pips and artifacts that were committed before it (so,
  same restriction as the regular pip graph building process).
- Service-related pips are unsupported in the initial dynamic mode. This
  includes service, client, shutdown, and finalization pips. Standalone IPC pips
  are not inherently incompatible.
- Shared opaque directories are unsupported until their graph-wide cleanup
  semantics are redesigned.
- Undeclared source reads are unsupported because their filesystem observations
  cannot be validated against an incomplete graph.
- Late output-directory existence assertions are unsupported.
- Pips may not add new pips for now. Existing frontends can publish through the
  shared mutable pip graph; executing pips are not graph producers yet.
- Graph-conformance validation must be performed incrementally whenever a pip
  is admitted. This includes dependency ordering, artifact producers, path
  ownership and overlap, directory seals, and pip-specific rules.
- While graph publication is open, graph-producing frontends and tools must not
  read paths that admitted pips may modify, unless that interaction is
  represented by an explicit publication barrier.

### Initially degraded or disabled scheduler functionality

- Exact downstream critical-path prioritization and end-of-build critical-path
  reporting are disabled because sinks and dependent closures are incomplete
  while publication is open. Dynamic scheduling uses explicit pip priority.
- Module affinity initialization is disabled because it builds the complete
  module-to-worker mapping from the final selected pip set. This could
  potentially be restored if module membership is known before publication.
- Process-remoting static-directory registration is disabled because it scans
  the complete selected pip set before execution to register every directory.
- Binary execution logging is disabled because its header requires the final
  graph ID and maximum serialized path index before pip events are written.
- Graph-wide diagnostics and exports that consume a finalized `PipGraph` are
  disabled. This includes the scheduler simulator, packed execution export,
  failed-pip dumps, and observed-input anomaly analysis.
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
- Remote historic metadata and running-time table persistence require the final
  graph semi-stable fingerprint for their cache keys.

### Non-obvious dynamic dependencies

- `FileSystemView.Create` pre-populates a cache using the final artifact count.
  Dynamic mode skips pre-population, so parent directories for newly admitted
  artifacts must be added to the filesystem view incrementally.
- API server creation checks the graph moniker once during scheduler startup.
  A moniker requested later requires a metadata notification or an upfront
  publication barrier.
- Graph-construction input tracking is disabled with graph caching. A frontend
  can observe an executing pip's output transition from absent to present,
  which is invalid under the input tracker's immutable-input model. A production
  protocol needs an explicit barrier or a separate class of execution signals.
- PipGraph.Builder.Build() does whole graph validation that potentially needs to 
  be done during dynamic pip addition.