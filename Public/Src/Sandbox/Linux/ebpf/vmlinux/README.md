## vmlinux.h

# Background
This directory contains an auto generated header from the [vmlinux](https://en.wikipedia.org/wiki/Vmlinux) binary which contains a compiled version of the Linux Kernel.
From this we can use [bpftool](https://github.com/libbpf/bpftool) to generate a header [vmlinux.h](vmlinux.h) that contains all the type definitions for the kernel that was used to generate it.
This file will change based on the kernel version used to generate it because types may between kernel versions. However, since we use [BPF CO-RE](https://docs.ebpf.io/concepts/core/), it allows our BPF programs to be portable between kernel versions.

Therefore, we want to check in the vmlinux header for the newest version of the kernel we support.

## Generating vmlinux.h
1. Set executable permissions on the [generate-vmlinux.sh](generate-vmlinux.sh) script inside this directory `chmod u+x generate-vmlinux.sh`.
2. Run [generate-vmlinux.sh](generate-vmlinux.sh).