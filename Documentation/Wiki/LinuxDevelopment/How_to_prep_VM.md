# How to prepare a fresh Linux VM

## Install Prerequisites
### Ubuntu 22.04 and 24.04
```bash
# install packages
# Note: libelf-dev, zlib1g-dev, and libnuma-dev are required by the eBPF sandbox (CODESYNC: Public/Src/Sandbox/Linux/ebpf/BuildXL.Sandbox.Linux.eBPF.dsc)
sudo apt-get update 
sudo apt-get install –y git build-essential libc6-dev openssh-server curl dotnet9 clang libelf-dev zlib1g-dev libnuma-dev

# link libdl.so
sudo ln -vs /lib/x86_64-linux-gnu/libdl.so.2 /usr/lib/x86_64-linux-gnu/libdl.so
```

### Mariner / Azure Linux
```bash
sudo dnf install -y rsync glibc-static.x86_64 time dotnet-sdk-9.0 clang elfutils-libelf elfutils-libelf-devel zlib-devel libnuma-devel
```

### Install .NET
```
wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb
sudo apt-get install -y dotnet-sdk-6.0 
```

## WSL kernels without task accounting

The eBPF sandbox uses `sched_process_exit` when its kernel BTF prototype exposes
`group_dead`, and falls back to `taskstats_exit` on older kernels. At least one
of these hooks is required: process exit reports are needed to drain file-access
reports and finish tracking the process tree. Disabling exit reporting is not safe.
Other required sandbox probes must also be supported by the running kernel;
this fallback only removes the dependency on task accounting for exit reports.

Readlink reporting prefers the `readlink` and `readlinkat` syscall entry/exit
tracepoints, avoiding dependency on a compiler-inlined or cloned `do_readlinkat`
symbol. Kernels without these tracepoints retain the original `do_readlinkat`
hooks. Symlink traversal uses `pick_link` when available in BTF, otherwise
`step_into` and `security_inode_follow_link`.

Kernels without `/proc/<pid>/io` can still provide CPU and memory measurements.
BuildXL preserves those measurements and reports zero I/O counters, with a
one-time trace diagnostic that I/O accounting is unavailable.
Availability is detected once using `/proc/self/io`; when it is absent, subsequent
samples skip I/O reads entirely while continuing to read CPU and memory data.

The eBPF sandbox also requires bpffs mounted at `/sys/fs/bpf`:

```bash
sudo mount -t bpf bpf /sys/fs/bpf
```

To mount it on subsequent starts, add `bpf /sys/fs/bpf bpf defaults 0 0` to
`/etc/fstab` if an equivalent entry does not already exist.

### Linux-only selfhost build

To build and deploy only the Linux developer engine, without restoring or
evaluating macOS native packages:

```bash
./bxl.sh --deploy-dev /p:[Sdk.BuildXL]linuxOnly=1 /server-
./bxl.sh --use-dev /p:[Sdk.BuildXL]linuxOnly=1 /server-
```

The flag is opt-in; default cross-platform package behavior is unchanged.
When bootstrapping on WSL with an older engine, append
`/unsafe_GlobalUntrackedScopes:/mnt/wsl/resolv.conf` if DNS resolution reports
that file as an undeclared dependency. The rebuilt engine includes this exact
WSL DNS configuration file in its Linux OS dependencies, so the override is
not needed afterward.

## Install/configure git credentials provider

* follow https://github.com/GitCredentialManager/git-credential-manager#linux
* run `git config --global credential.credentialStore secretservice` (might require GUI)
* run `git config --global credential.helper cache && git config --global credential.helper 'cache --timeout=600000`

## Device code authentication
In order to do device code authentication in a Linux headless terminal, a security exception needs to be requested. Check this [link](https://eng.ms/docs/microsoft-security/ciso-organization/iamprotect/enterprise-iam/productivity-environment/tsgs/devicecodeflowdcfrestrictions) for details. Summarizing:
* Read the TSG linked above
* Watch the recommended training [video](https://microsoft.sharepoint.com/:v:/t/MicrosoftSecurityDayofLearningArchive/IQC2eIuoiE0rT7HzOObKnIjvATSBclBLrhVsuyypm9cAyBQ?e=lcPgIP)
* Request an exception [here](https://aka.ms/dcfexception) (this has to be renewed every 30 days)
* Connect to a Microsoft network (AzVPN, GSA, CorpNet, or Azure) before running any DCF-based sign-in.