# BuildXL Cross Platform

* BuildXL is fully supported for Windows.
* Linux: see [Readme.md](../../README.md) for supported distros.
* macOS is not currently supported.

## Linux Support

### Prerequisites
On Linux, BuildXL uses an eBPF-based sandbox for file access monitoring. This requires the following native shared libraries to be installed on the system:

<!-- CODESYNC: Public/Src/Engine/Processes/SandboxConnectionLinuxEBPF.cs (s_requiredNativeLibraries) -->
<!-- CODESYNC: Public/Src/Sandbox/Linux/ebpf/BuildXL.Sandbox.Linux.eBPF.dsc (libraries list in link step) -->

| Library | Ubuntu/Debian | Mariner/Azure Linux |
|---------|---------------|---------------------|
| `libelf` | `libelf1` | `elfutils-libelf` |
| `zlib` | `zlib1g` | `zlib` |
| `libnuma` | `libnuma1` | `numactl-libs` |

```bash
# Ubuntu/Debian
sudo apt install libelf1 zlib1g libnuma1

# Mariner/Azure Linux
sudo dnf install elfutils-libelf zlib numactl-libs
```

BuildXL will check for these libraries at startup and report a clear error message if any are missing.

### Limitations

The following features are not supported on the PTrace sandbox.
- Blocking disallowed file accesses

## macOS Support History
In the past there was a push to bring BuildXL to macOS to provide cached and distributed builds to the a number of Microsoft teams. BuildXL moved to .netcore and scrubbed the codebase to add Unix support. The core bxl executable can be cross compiled on Windows to run on macOS. The major component that needed to be rewritten for macOS is the file access monitoring layer. This is what allows BuildXL to provide reliable caching.

There are a number of options for monitoring process trees and the files they access on unix platforms, and slightly fewer on macOS. Thorough analysis and prototyping was performed and all existing frameworks had issues that prevented their use. The last resort was writing a custom Kernel Extension (KEXT). This was able to satisfy the requirements for high performance and lossless file access tracking for child process trees. It enabled moving forward with macOS support but it came with the risk of of using a technology that might not be supported long term.

In 2020, it was announced that KEXT support would be deprecated from new versions of macOS. Apple provided a replacement for the core functionality in [Endpoint Security](https://developer.apple.com/documentation/endpointsecurity). A prototype Endpoint Security based monitoring sandbox for BuildXL has been implemented, but at the time it proved to drop too many events to be practical for our main use case. That use case was primarily C++ and various scripts where including probes to non-existing files was important for the correctness of caching. Those probes were the highest volume of events which caused the Endpoint Security to be lossy. Between this and competing priorities on the build graph translation work, the decision was made to cease the effort.

Porting BuildXL to macOS helped jump start the team's other cross platform investments, namely Linux. The process execution and monitoring layer is common now across many Microsoft build products, including the Windows and Linux monitoring layer. The macOS layer remains in the state of being implemented, but too lossy for practical use.

The BuildXL team is aware of the demand for build acceleration on macOS and is contemplating ways to move forward.