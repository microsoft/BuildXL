BuildXL constructs pip graphs by parsing and evaluating the DScript specifications (or specs). When there are a lot of specs, the graph construction can take some time.
Luckily, BuildXL has a capability of reusing pip graphs that were constructed in the previous builds. To allow graph reuse, users need to ensure that the inputs used
to construct the pip graph are stable. These inputs include the evaluated spec files, enumerated directories, the environment variables queried (read/probed) by the spec files during
evaluation, and the mounts specified in the build. 

# Pip graph (weak) fingerprint

One key used by BuildXL to identify a pip graph is a so-called *pip graph weak fingerprint*. If the fingerprint changes, then the pip graph cannot be reused.

Pip graph weak fingerprint consists of
- config file,
- build qualifiers,
- evaluation filters,
- layout of source, object, and temp directories,
- subst target,
- build engine hash or commit id, and
- host OS, CPU architecture, and elevation level.

These elements can affect the pip graph being constructed. For example, if the layout of source or object directories change, then the paths occurring the graph
can change. Similarly for subst target. For example, if the subst target changes from `B:` to `A:`, then all paths in the graph produced during evaluation will
start with `A:` instead of `B:`.

# Inputs for constructing pip graphs

When BuildXL evaluates the specs to construct a pip graph, it keeps track of elements that affect the shape and content of the constructed pip graph. Those elements are
- the evaluated spec files (or any read file) and their content hashes,
- the enumerated directories and the hashes of their memberships,
- the queried environment variables and their values, and
- the used mounts and their paths.

These elements constitute *pip graph inputs*.

During evaluation, the spec can glob a directory, and the resulting file list is passed as dependencies for a pip. Thus, if the membership of the globbed directory
changes (due to file addition/removal), then the pip specification itself will change, and the pip graph cannot be reused.
Also, during evaluation, the spec can call `Environment.getStringValue(...)` or `Environment.hasVariable(...)` that can determine the shape of the pip graph. For example,
if `Enviroment.hasVariable("DROP_ENABLED")` returns true, then the pip graph will have extra drop pips. The result of `Environment.getStringValue("CXX_FLAGS")` can
be embedded in the arguments of some pips, and thus changing the value of `%CXX_FLAGS%` affect the constructed pips. Similarly for mounts: the spec can call `Context.getMount(...)` whose result may be used in some pip specification.

# Engine cache vs. real cache

The reused pip graph can come from the engine cache, a folder typically set in the object or output directory, or from the real cache. 
The former stores only the last recently constructed graph, while the latter can have many pip graphs from previous builds. Because it is cheap to check
the engine cache, BuildXL will first try to determine if the pip graph in the engine cache can be reused before it looks up for one in the real cache.

To enable checking the engine cache, BuildXL persists the pip graph weak fingerprint and the graph inputs into a file in the engine cache. In the next build,
the file is read, and if the new weak fingerprint is different from the persisted one, or if there is any change in the graph input, then the pip graph in the engine cache
cannot be reused; otherwise, the pip graph can be reused and BuildXL does not bother checking the real cache. BuildXL leverages USN change journal to avoid hashing the spec files 
for checking the pip graph inputs, i.e., besides tracking the content hashes of the evaluated spec files, BuildXL also keeps track of the USNs of those files.

If the pip graph in the engine cache cannot be reused, then BuildXL will consult the real cache. The pip graph weak fingerprint and the hash of the pip graph inputs
become the key of the cache look up.

# Evaluation filter

Users may pass different evaluation filters when constructing the pip graph. The evaluation filters only make the constructed pip graph a subgraph of the one that is constructed
without any evaluation filter. Thus, the latter graph is actually reusable when the evaluation filters are applied.

# Making pip graph caching work

To have pip graph cache hit, users need to make the pip graph weak fingerprint and the pip graph inputs stable build over build. Here are some guidelines to make the graph inputs stable.

## Do not query constantly changing environment variables

Typical case of using constantly changing environment variable is using an environment variable to keep track of build id. For example,
```
const buildId = Environment.getStringValue("BUILD_ID"); 
```
where `%BUILD_ID%` is set by a batch script before that script invokes a build, e.g., 
```
# REM Get build id based on the current time.
set BUILD_ID=Build_%date:~7,2%_%date:~4,2%_%date:~10,4%_%time:~0,2%_%time:~3,2%

# Invoke build
bxl /c:config.dsc ...
``` 

## Use BuildXL's log folder mount for current build's log

Related to the constantly changing environment variable, users typically invent their own log folders based on the current time. For example,
```
Transformer.execute({
    ...
    outputs: [p`${Environment.getPathValue("LOG_PATH")}/pip123.log`]
    ...
});
```
where `%LOG_PATH%` is set by a batch script before that script invokes a build, e.g., 
```
# REM Get log path based on the current time.
set LOG_PATH=Log_%date:~7,2%_%date:~4,2%_%date:~10,4%_%time:~0,2%_%time:~3,2%

# Invoke build
bxl /c:config.dsc ...
```

Instead of using user-specified log folders, use BuildXL's mount, e.g.,
```
Transformer.execute({
    ...
    outputs: [p`${Context.getMount("LogsDirectory")}/pip123.log`]
    ...
});
```

## Enable user-profile redirection

For graph caching across machines, do not query or embed user profile or domain in the pip specification without enabling user-profile redirection.
For example, 
```
Transformer.execute({
    ...
    untrackedPaths: [p`${Environment.getPathValue("USERPROFILE")}/abc.txt`]
    ...
})
```
The value of `Environment.getPathValue("USERPROFILE")` will be different from one user to another.

BuildXL provides a so-called user-profile redirection that will redirect the values of `%USERPROFILE%` and related environment variables
to stable values. To this end, BuildXL creates a junction from a stable path to the real user profile path.