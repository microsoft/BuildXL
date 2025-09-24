// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <stdio.h>
#include <unistd.h>
#include <errno.h>
#include <cstdint>
#include <linux/types.h>
#include "ebpfcommon.h"
#include "test_utils.hpp"

/**
 * Sends two synthetic EBPF probes for the specified paths.
 * Probes are guaranteed to run on the same CPU and the kernel last path cache is cleaned up before the first probe is sent
 * Expected arguments: path1 path2
 */
int main(int argc, char **argv) {

    test_incremental_event_args args;

    strncpy(args.path1, argv[1], PATH_MAX);
    strncpy(args.path2, argv[2], PATH_MAX);

    fprintf(stdout, "Sending synthetic EBPF probes for paths: %s, %s\n", args.path1, args.path2);

    LIBBPF_OPTS(bpf_test_run_opts, test_run_opts,
        .ctx_in = &args,
        .ctx_size_in = sizeof(args),
    );

    int write_event = GetTestProgramFd("test_incremental_event");
    if (write_event < 0) {
        fprintf(stderr, "[%s]Failed to get fd for test_incremental_event program: %s\n", argv[0], strerror(errno));
        return 1;
    }

    fprintf(stdout, "Test program retrieved\n");

    bpf_prog_test_run_opts(write_event, &test_run_opts);

    if (test_run_opts.retval != 0) {
        fprintf(stderr, "Failed to test run test_write_event: %d - %s\n", test_run_opts.retval, strerror(test_run_opts.retval));
        return 1;
    }

    fprintf(stdout, "Success\n");

    return 0;
}
