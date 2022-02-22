## BuildXL.trace file

Sometimes there is a need to have high-level picture of how a build looked like. While it is possible to infer all the information from various text log files, this is a somewhat time-consuming exercise. To address this problem, one can use LogTracer feature (controlled via `/logTracer` argument). When enabled, BuildXL will create a `.trace` file in the log directory. The file contains a few views - CPU and  RAM utilization during a build, as well as, executed pips (pips that took less than 30 seconds to execute are discarded for usability purposes).

The file itself is just a JSON blob that has the same format as Chromium's Trace Event Profiling Tool. Because of that, the .trace file is easily viewable in any Chromium-based web browser - for example, in Microsoft Edge, navigate to `edge://tracing`, click Load in the top left corner, and select the produced .trace file.

We currently show only process and IPC pips for CacheLookup, MaterializeInput, and ExecuteProcess if the step takes more than 30seconds. For each worker, you will find 4 processes: 
Process # (MachineName) – Counters => CPU and RAM percent counters on the machine
Process # (MachineName) – CacheLookup => showing process pips running CacheLookup step
Process # (MachineName) – Materialize => showing process pips running MaterializeInput step
Process # (MachineName) – CPU => showing process pips running ExecuteProcess step
Process # (MachineName) – Light => showing IPC pips running ExecuteNonProcess step

Process #0 (Local) -> Orchestrator machine
Process #N (MachineName) -> Worker machine. 

Under each process, each row represents a slot in our dispatcher logic, so you can consider it as a thread. 

When you click the pip on the timeline, you will see some metadata. The metadata currently contains: duration, the step, short description. Those are all indexed and quick-searchable. The trace files are generated on each worker, but the main worker’s trace file will contain events from all workers; so you do not have to download and load separate trace files for the same build. If you think that it might be useful for dev builds, we can write a selenium script that loads the trace file every 30 seconds on the browser while the build is progressing.  
