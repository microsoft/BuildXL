// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <stdio.h>
#include <unistd.h>
#include <errno.h>
#include <linux/types.h>
#include "ebpfcommon.h"
#include "test_utils.hpp"

/**
 * Sends one synthetic EBPF probe for the specified path after canonicalizing it.
 * Expected arguments: path
 */
int main(int argc, char **argv) {

    test_path_canonicalization_args args;

    strncpy(args.path, argv[1], PATH_MAX);

    fprintf(stdout, "[%s]Sending synthetic EBPF probe for path: %s\n", argv[0], args.path);

    LIBBPF_OPTS(bpf_test_run_opts, test_run_opts,
        .ctx_in = &args,
        .ctx_size_in = sizeof(args),
    );

    int write_event = GetTestProgramFd("test_path_canonicalization");

    if (write_event < 0) {
        fprintf(stderr, "[%s]Failed to get fd for test_path_canonicalization program: %s\n", argv[0], strerror(errno));
        return 1;
    }

    fprintf(stdout, "Test program retrieved\n");

    bpf_prog_test_run_opts(write_event, &test_run_opts);

    if (test_run_opts.retval != 0) {
        fprintf(stderr, "Failed to test run test_path_canonicalization: %d - %s\n", test_run_opts.retval, strerror(test_run_opts.retval));
        return 1;
    }

    fprintf(stdout, "Success\n");

    return 0;
}
