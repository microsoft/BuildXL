BuildXL aims to prioritize the critical path of a build to get the shortest end to end build time.

After BuildXL constructs or loads a build graph, it makes a pass across all pips that are filtered in and uses a heuristic for how long it expects the pip to run. With no additional information, that heuristic is based on the count of declared input and output files. The expected runtime for a pip will contain the expected runtime for all downstream pips. This expected runtime becomes the priority. It is static once assigned at the beginning of the execute phase.

While the execute phase runs, the pip with the highest priority will be executed first if more than one pip is ready to be run.

After the build is completed, the actual per pip runtimes are serialized to a file so future builds can use that data instead of the heuristic. The data is also added to the cache; builds utilizing a shared datacenter cache access historical runtime information as well. This of course wouldn't work well if the machines putting to the cache were not homogeneous.

There is experimental support to manually override the priority of a pip when adding it to the build graph. The field is an int and its value is heavily intertwined to with the implementation of BuildXL's heuristics described above. The most straightforward way to model this is to think of the priority as the number of milliseconds this pip plus all downstream consumers of this pip's outputs would take to run.
```ts
    let execArguments : Transformer.ExecuteArguments = {
        tool: args.tool || tool,
        tags: args.tags,
        arguments: arguments,
        // ...
        priority: 1,
    };
```
