#!/bin/bash

set -e

# This script is used to generate a vmlinux file from a vmlinux ELF file.
# First it will create a temporary directory under /tmp and download bpftool from https://github.com/libbpf/bpftool/releases/download/v7.5.0/bpftool-v7.5.0-amd64.tar.gz.
# Then it will untar the downloaded tar.gz file under the /tmp/bpftool directory.
# Next it will use bpftool to generate a new vmlinux file from the vmlinux ELF file.
# The output file will be named vmlinux_<kernel major ver><kernel minor ver>.h
# If the minor version has a single digit, it will be prefixed with a 0.
# The output file will be placed in the same directory as this script.
# The script will then create a symlink to the newly generated file with the name vmlinux.h in the same directory.

output_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
kernel_version=$(uname -r)
kernel_major_ver=$(echo $kernel_version | cut -d. -f1)
kernel_minor_ver=$(echo $kernel_version | cut -d. -f2)

if [ ${#kernel_minor_ver} -eq 1 ]; then
    kernel_minor_ver="0${kernel_minor_ver}"
fi

output_file="${output_dir}/vmlinux_${kernel_major_ver}${kernel_minor_ver}.h"

# Delete any existing vmlinux header files before proceeding.
rm -f "${output_dir}"/vmlinux_*.h "${output_dir}/vmlinux.h"

# Remove previous temporary bpftool directory if it exists
if [ -d "${output_dir}/tmp_bpftool" ]; then
    rm -rf "${output_dir}/tmp_bpftool"
fi
mkdir -p "${output_dir}/tmp_bpftool"
tmp_dir="${output_dir}/tmp_bpftool"
# Sets up a trap so that when the script exits, it automatically removes the temporary bpftool directory under the script's directory
trap "rm -rf \"${output_dir}/tmp_bpftool\"" EXIT

# Download and extract bpftool
bpftool_url="https://github.com/libbpf/bpftool/releases/download/v7.5.0/bpftool-v7.5.0-amd64.tar.gz"
curl -L $bpftool_url -o $tmp_dir/bpftool.tar.gz
mkdir -p $tmp_dir/bpftool
tar -xzf $tmp_dir/bpftool.tar.gz -C $tmp_dir/bpftool
chmod +x "$tmp_dir/bpftool/bpftool"

# Generate the vmlinux header file using the default ELF file so that the output is under this script's directory.
$tmp_dir/bpftool/bpftool btf dump file "/sys/kernel/btf/vmlinux" format c > "$output_file"

# Prepend copyright notice to the generated header file.
{
    echo "// Copyright (c) Microsoft Corporation"
    echo "// SPDX-License-Identifier: GPL-2.0 OR MIT"
    cat "$output_file"
} > "$output_file.tmp" && mv "$output_file.tmp" "$output_file"

# Create a symlink to the generated header file using relative paths
(cd "${output_dir}" && ln -sf "$(basename "$output_file")" "vmlinux.h")

echo "Generated $output_file and created symlink vmlinux.h"