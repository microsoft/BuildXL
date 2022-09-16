# How to run unit tests
To run all unit tests (i.e., all pips tagged 'test')
```bash
./bxl.sh --internal "/f:tag='test'"
```

Standard bxl pip filtering options apply.

To conveniently filter by test class or method name, you can use use the shortcut `--test-class` and `--test-method` switches provided by the `bxl.sh` script.

# How to debug a `bxl` process running on Linux from Visual Studio running on Windows

* (Linux) set `BuildXLDebugOnStart` env var to `1` before starting your `bxl` process
    * e.g., `env BuildXLDebugOnStart=1 ./bxl.sh ...`
    * this won't automatically attach a debugger; instead it will pause and print out a 'press Enter when the debugger is attached' message
* (Windows) follow [remote-debugging](https://docs.microsoft.com/en-us/visualstudio/debugger/remote-debugging-dotnet-core-linux-with-ssh?view=vs-2022) instructions to attach to your `bxl` process from Visual Studio over SSH
    * make sure your local source code is in sync with what you are debugging and that symbols match
    * set breakpoints and stuff before proceeding
* (Linux) press Enter to resume the `bxl` process
    * the process will break in your Visual Studio instance if it hits any of the set breakpoints

# How to debug unit tests
The most straightforward (but not very convenient) method is running a test from the command line (see above) and then inspecting the produced log files.

Some options for trying to get the VSCode debugger to work:
* generate csproj files (e.g., `./bxl.sh --internal --minimal --vs`), then try to load the projects in VSCode, then try to run unit tests directly from VSCode
* build a test assembly of interest with BuildXL then create a VSCode task (in `tasks.json`) to run/debug that assembly with XUnit

Debugging from Visual Studio over SSH (see the previous section) could also be an option.

# How to test/debug the sandbox as a standalone library
```bash
set -eu

# build the sandbox binaries
./bxl.sh --internal "/f:output='*/Out/Bin/debug/linux-x64/lib*'"

# run any process (e.g., 'ls -l') with LD_PRELOAD set to newly build 'libDetours.so' and __BUILDXL_LOG_PATH to an empty log file
echo > bxl.log
env 
  __BUILDXL_LOG_PATH="$(pwd)/bxl.log" \
  LD_PRELOAD="$(pwd)/Out/Bin/debug/linux-x64/libDetours.so" \
  ls -l

# inspect the bxl.log file to see reported accesses
```
