#!/bin/bash

readonly MY_DIR=$(cd `dirname ${BASH_SOURCE[0]}` && pwd)
source "${MY_DIR}/env.sh"

# check if SIP is disabled, fail if not.
readonly sipStatus="$(csrutil status)"
print_info "$sipStatus"
if [[ -z $(echo "$sipStatus" | grep -o "disabled") ]]; then
    print_error "SIP is not disabled"
    exit 1
fi

/bin/bash "${MacOsScriptsDir}/bxl.sh"       \
  --config "$MY_DIR/config.dsc"             \
  --symlink-sdks-into "$MY_DIR/sdk"         \
  --buildxl-bin "$BUILDXL_BIN"              \
  /useHardLinks+                            \
  /disableProcessRetryOnResourceExhaustion+ \
  /exp:LazySODeletion+                      \
  /logObservedFileAccesses+                 \
  "$@"