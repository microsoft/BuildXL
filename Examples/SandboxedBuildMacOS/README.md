# Introduction
A simple "HelloWorld"-type clang C++ build for validating the sandbox functionality with BuildXL on the Mac. Note: The validaton will fail if executed with sandboxing disabled.

# Getting Started
* Disable SIP on your mac

* Set `BUILDXL_BIN` environment variable to point to a BuildXL .NET Core deployment (binaries), e.g.,
```bash
    unzip BuildXL.osx-x64.0.1.-20190101.1.nupkg -d buildxl_bin
    export BUILDXL_BIN=$(cd buildxl_bin && pwd)
```

* Depending on the sandbox kind you want to validate, either load the sandbox kernel extension or start the sandbox daemon process before running. Pass `--load-kext` to load the kernel extension or `--install-daemon` to install a launchd daemon when calling the validation script.

* Simply run `./validate-build.sh /sandboxKind:SANDBOX_TYPE --REQ` to run the example with some validation, e.g., `./validate-build.sh /sandboxKind:MacOsDetours --install-daemon`

```bash
Supported sandbox kinds and requirements:

  SANDBOX_TYPE			      	REQ

  MacOsKext						--load-kext
  MacOsEndpointSecurity			--install-daemon
  MacOsDetours					--install-daemon
```

* The build logs all observed file access from the ES sandbox into the main BuildXL log file (check `./ESSandboxBuildMacOS/Out/BuildXLCurrentLog/BuildXL.log`), this can be used to verify correctness of the sandbox and expected observed file accesses.

* If everything goes well, the output should look something like:
```
 ===============================================================================
 === 1st run: clean build
 ===============================================================================
[info] System Integrity Protection status: unknown (Custom Configuration).

Configuration:
	Apple Internal: disabled
	Kext Signing: disabled
	Filesystem Protections: disabled
	Debugging Restrictions: disabled
	DTrace Restrictions: disabled
	NVRAM Protections: disabled
	BaseSystem Verification: enabled

This is an unsupported configuration, likely to break in the future and leave your machine in an unknown state.
[info] Checking BuildXL bin folder
[info] BUILDXL_BIN set to  /Users/userName/work/buildxl_bin
[info] Installing sandbox daemon from: '/Users/userName/work/buildxl_bin/native/MacOS/BuildXLSandboxDaemon'
29588	0	com.microsoft.buildxl.sandbox

[info] Symlinking sdk folder from BuildXL deployment: .../Examples/DotNetCoreBuild/sdk/Sdk.Transformers -> $BUILDXL_BIN/Sdk/Sdk.Transformers
[info] Running bxl: '/Users/userName/work/buildxl_bin/bxl' /enableIncrementalFrontEnd- /useHardLinks- /p:BUILDXL_BIN=/Users/userName/work/buildxl_bin /p:DOTNET_EXE=/usr/local/bin/dotnet /p:MONO_EXE=/usr/local/bin/mono /useHardLinks+ /sandboxKind:macOSEndpointSecurity /disableProcessRetryOnResourceExhaustion+ /exp:LazySODeletion+ /logObservedFileAccesses+
Microsoft (R) Build Accelerator. Build: 0.1.0-20211211.0, Version: [refs/heads/master:a9525cd65e2776d0f49bf54a1ea35a645896839b]
Copyright (C) Microsoft Corporation. All rights reserved.

[0:05] 100.00%  Processes:[2 done (0 hit), 0 executing, 0 waiting]
[0:05] -- Cache savings: 0.000% of 2 included processes. 0 excluded via filtering.
Build Succeeded
	Log Directory: /Users/krisso/Work/BuildXL.Internal/Examples/ESSandboxBuildMacOS/Out/Logs
BuildXL Succeeded
[info] Asserting build produced observable outputs...
```