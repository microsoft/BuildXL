#!/bin/bash

set -euo pipefail

__dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SANDBOX_ROOT="$(cd "${__dir}/.." && pwd)"

if ! which docker > /dev/null; then
    echo "*** ERROR *** 'docker' not found.  Install Docker first"
    exit 1
fi

echo "---------- Pulling manylinux2014_x86_64 ----------"
docker pull quay.io/pypa/manylinux2014_x86_64

function onExit {
    sudo chown -R `whoami`: "${__dir}/bin"
}

trap onExit EXIT

echo "---------- Building ----------"
JOBS=$(which nproc > /dev/null && nproc || echo 2)
docker run                                 \
    --rm -it                               \
    -v ${SANDBOX_ROOT}:/src                \
    -w /src/${__dir##$SANDBOX_ROOT/} \
    quay.io/pypa/manylinux2014_x86_64      \
    make all -j${JOBS}
