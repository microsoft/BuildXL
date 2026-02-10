### BuildXL eBPF Sandbox

## Coding style
# BPF programs
- BPF programs that trace a function should use the following convention on enter `<function name>_enter` and the following on exit `<function name>_exit`

## Notes
- The `SEC(?...)` convention in libbpf will prevent the specified program from autoloading. Reference: https://github.com/libbpf/libbpf/blob/42a6ef63161a8dc4288172b27f3870e50b3606f7/src/libbpf.c#L822C1-L826C1
- `SEC(?...s/...)` convention indicates that this program is sleepable. While it doesn't mean that the bpf program itself will sleep, it allows us to call sleepable helpers such as `bpf_copy_from_user`.
    - To find out whether a program is sleepable, refer to the table on the [Program Types](https://docs.kernel.org/bpf/libbpf/program_types.html) documentation
- Using tracepoint programs requires `CAP_DAC_OVERRIDE` to be set or to be run as root so that it can read from `/sys/kernel`.
- Find whether the current kernel supports tracing a specific function (ie: the function has BTF type information): `bpftool btf dump file /sys/kernel/btf/vmlinux format raw | grep <function name>`
    - Example output if the function has BTF type information: `[130644] FUNC 'pick_link' type_id=130643 linkage=static`

## Verifier tips
- The verifier is pretty strict when it comes to accessing arrays. General tips:
    - Copy to a temp array of known size (bpf_core_read). That array can be defined as an ebpf map (otherwise the verifier will complain about arrays that are too long)
    - Keep the length of the bpf_core_read around and use it to exit early if the index you are going to use is outside [0, length - 1] 
    - Whenever indexing happens, AND the index with the max size - 1 of the array (assuming an array with length equals to a power of 2). E.g. myArray[index & (MAX_SIZE - 1)]. This helps the verifier make sure the indexing always happens within bounds
    - The compiler likes to optimize things like (index + 1) & (MAX_SIZE - 1) in a way the verifier is not able to keep up. You can use volatile asm instructions in this case to bypass the optimization, check for example deref_path_info

## Future performance work
### Caching absent probes on kernel side
Most common operations are guarded with a kernel-side cache that makes sure we don't send the same operation (for the same path) over and over for the same pip. This cache is a lossy LRU-based per-pip map that uses the internal dentry + mount + inode number as key (check event_cache for details). This cache has typically a pretty high rate (90+%) and a visible impact in performance. The outstanding operation that is not guarded by this cache is path_lookupat. This operation is slightly different from the rest as it only takes care of absent probes, where there is no dentry/mount representing that path. There is a raw string cache in place, but that is only used when the ring buffer is under pressure and at risk of overflowing, since this cache is known to be very slow. 

First, how can we get a sense of how significant these events are? This data can be obtained by running with `/pipProperty:Pip53B84EFD5D2E4A47[EnableProcessVerboseLogging]` and looking at the 'kernel function' messages and its types, e.g. for `do_readlinkat`: `[2:32.750] verbose DX10101: [Pip53B84EFD5D2E4A47] Detours Debug Message: Ext: False, Out: False, Err: False, Rep: False :: [317524] (available: 99.86%) kernel function: do_readlinkat, operation: readlink, S_ISREG: 0, S_ISDIR: 0, errno: 0, CPU id: 12, common prefix length: 48, incremental length: 0, path: '/home/foobar/.nvm/versions/node/v22.18.0/bin/yarn'`.

For example, this is the number of events per type for a (mostly random) test pip after the kernel-side cache has filtered events out:

| Event type | # of events | percentage |
|------------|-------------|------------|
|Total | 2,863,277.00 | 100% |
|do_faccessat | 96,067.00 | 3.36% |
|path_lookupat | 2,022,336.00 | 70.63% |
|security_file_permission | 453,989.00 | 15.86% |
|security_file_open | 96,863.00 | 3.38% |
|inode_getattr | 47,785.00 | 1.67% |
|readlink | 98,078.00 | 3.43% |

The other indicator is the general stats emitted by the ring buffer when the pip is done 
```
[3:57.316] verbose DX14308: [Pip53B84EFD5D2E4A47] [Ring buffer monitoring] Event cache hit: 12052868 (94.30%), Event cache miss: 728118
[3:57.316] verbose DX14308: [Pip53B84EFD5D2E4A47] [Ring buffer monitoring] Minimum available space: 1761.52 KB (86.01%). Total available space: 2048.00 KB. Total bytes sent: 74793.23 KB. Total events 2863277. Capacity exceeded 0 time(s).
```
This is showing that 2,863,277 events were sent from kernel side to user side. Only 728,118 of those events were a cache miss, pointing to path_lookupat as the main culprit, which doesn't check the path cache. On the other hand, we can also see how many of these events ended up being relevant for managed side. There is a user-side cache as well, without the limitations of the kernel side one, which is our second filter for repeated events. We can look at how many absent probes ended up passing this second cache by checking how many messages with `Access report received: Probe` and return code 2 (`...|2|False|/home...`) we get. For the above pip, there are ~110k absent probes like this. Which means we have the potential to avoid sending ~1.9M events from kernel side to user side for this pip.

Some ideas to deal with this issue:
* Most of the slowness of the string cache comes from the fact that the key of the cache is a 512B string (not 4k - we only cache strings that are not too long). We could try to shorten this even further by looking at the last resolved dentry in the path walk and use something like 'last present dentry + remaining absent string'. The rationale is that most absent probes happen under the repo root and a good chunk of the absent path ancestors are actually present.