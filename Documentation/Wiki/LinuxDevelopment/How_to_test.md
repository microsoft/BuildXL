# How to simply test *libDetours.so*
* `cd Public/Src/Sandbox/Linux`
* `make`
* `echo > libDetours.log`
* `env __BUILDXL_LOG_PATH=$(pwd)/libDetours.log LD_PRELOAD=$(pwd)/bin/debug/libDetours.so ls -l`

# How to run BuildXL unit tests on Linux
## Build unit test binaries on a Windows Machine
We currently do not support building BuildXL banaires in Linux machines. BuildXL unit test binaries need to be built in Windows.
* `bxl /q:DebugLinux /f:output='out/bin/linux-x64-tests/*'`

## Build Linux sandbox binaries on a Linux Machine/VM
* `cd Public/Src/Sandbox/Linux`
* `make`

## Deploy test binaries and Linux sandbox binaries to a Linux Machine/VM
*Example:*
* `scp -r Out/Bin/linux-x64-tests/debug/* AnyBuildTestVM:~/bxl-tests-linux-x64/`
* `scp -r Public/Src/Sandbox/Linux/bin/debug/*.so AnyBuildTestVM:~/bxl-tests-linux-x64/TestProj/tests/shared_bin`

## Run tests
* `cd ~/bxl-tests-linux-x64/`
* `./bash_runner.sh`

## Logs
* Unit test logs can be found in *~/bxl-tests-linux-x64/TestProj/tests/shared_bin/XunitLogs*
* Linux sandbox log can be found in */tmp* with name similar to *bxl_Pip1234.31870.log*