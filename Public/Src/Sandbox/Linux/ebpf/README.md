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