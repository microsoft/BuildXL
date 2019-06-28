#!/bin/bash

readonly MY_DIR=$(cd `dirname ${BASH_SOURCE[0]}` && pwd)

source ${MY_DIR}/env.sh
readonly CACHE_ROOT=${MY_DIR}/out/casaas
readonly CACHE_STDOUT="${CACHE_ROOT}/casaas.stdout"
readonly CACHE_NAME=example-build-casaas-2
readonly CACHE_SCENARIO=scenario-$CACHE_NAME

function printRunningCasaasPid {
    ps -ef | grep ContentStoreApp | grep -v grep | awk '{print $2}'
}

function killRunningContentStoreApp {
    local runningCasaasPid=$(printRunningCasaasPid)
    if [[ -n $runningCasaasPid ]]; then
        print_info "ContentStoreApp process running: ${runningCasaasPid}. Sending TERM..."
        kill -s TERM $runningCasaasPid
        sleep 1
        local checkAgainPid=$(printRunningCasaasPid)
        if [[ -n $checkAgainPid ]]; then
            print_error "Cound not kill ContentStoreApp process"
            return 1
        fi
    fi
}

function createContentStoreAppSettingsFile {
    local file=$(mktemp -t casaas-settings-json)
    cat > $file <<EOF
{
    "IsDistributedContentEnabled": "true",
    "ConnectionSecretNamesMap": {
        ".*": {
            "RedisContentSecretName": "CloudStoreRedisConnectionString",
            "RedisMachineLocationsSecretName": "CloudStoreRedisConnectionString"
        }
    },
    "KeySpacePrefix": "PD",
    "ContentHashBumpTimeMinutes": 1500,
    "IsBandwidthCheckEnabled": true,
    "IsDistributedEvictionEnabled": true,
    "IsPinBetterEnabled": true,
    "PinRisk": 1.0E-8,
    "FileRisk": 0.02,
    "MachineRisk": 0.08,
    "IsPinCachingEnabled": true,
    "IsTouchEnabled": true,
    "IsRepairHandlingEnabled": true,
    "UseLegacyQuotaKeeperImplementation ": false,
    "PinRisk ": null,
    "FileRisk ": null,
    "MachineRisk ": null,
    "IsPinCachingEnabled ": false,
    "IsTouchEnabled ": false,
    "IsContentLocationDatabaseEnabled ": false,
    "GlobalRedisSecretName ": "cbcache-test-redis-{StampId:LA}",
    "SecondaryGlobalRedisSecretName ": "cbcache-test-redis-secondary-{StampId:LA}",
    "EventHubSecretName ": "cbcache-test-eventhub-{StampId:LA}",
    "AzureStorageSecretName ": "cbcacheteststorage{StampId:LA}",
    "MaxEventProcessingConcurrency ": 64,
    "UseIncrementalCheckpointing ": true,
    "UseDistributedCentralStorage ": true,
    "LocationEntryExpiryMinutes ": 120,
    "IsReconciliationEnabled ": true,
    "ContentLocationReadMode ": "LocalLocationStore",
    "ContentLocationWriteMode ": "Both",
    "UseMdmCounters ": true,
    "UseTrustedHash ": true,
    "TrustedHashFileSizeBoundary ": 100000,
    "IsMachineReputationEnabled ": true,
    "ParallelHashingFileSizeBoundary ": 52428801,
    "EmptyFileHashShortcutEnabled ": true,
    "BlobExpiryTimeMinutes ": 30,
    "MaxBlobCapacity ": 3221225472,
    "IsGrpcCopierEnabled ": true
}
EOF
    echo $file
}

function createCacheConfigJson {
    local cacheConfigFile=$(mktemp -t cache-config-json)
    cat > $cacheConfigFile <<EOF
{
    "LocalCache": {
        "CacheId": "SelfhostCS2L1",
        "Assembly": "BuildXL.Cache.MemoizationStoreAdapter",
        "CacheLogPath": "[BuildXLSelectedLogPath]",
        "Type": "BuildXL.Cache.MemoizationStoreAdapter.MemoizationStoreCacheFactory",
        "CacheRootPath": "${CACHE_ROOT}/cache",
        "UseStreamCAS": false,
        "EnableContentServer": true,
        "EmptyFileHashShortcutEnabled": false,
        "CheckLocalFiles": false,
        "CacheName": "${CACHE_NAME}",
        "GrpcPort": 7089,
        "Scenario": "${CACHE_SCENARIO}"
    },
    "RemoteCache": {
        "Type": "BuildXL.Cache.BuildCacheAdapter.DistributedBuildCacheFactory",
        "Assembly": "BuildXL.Cache.BuildCacheAdapter",
        "CacheId": "RemoteCache",
        "CacheLogPath": "[DominoSelectedLogPath].L3.log",
        "CacheServiceFingerprintEndpoint": "https://artifactsu0.artifacts.visualstudio.com/DefaultCollection",
        "CacheServiceContentEndpoint": "https://artifactsu0.vsblob.visualstudio.com/DefaultCollection",
        "UseBlobContentHashLists": true,
        "CacheKeyBumpTimeMins": 120,
        "CacheNamespace": "${CACHE_NAME}",
        "CacheName": "${CACHE_NAME}",
        "ConnectionRetryCount": 5,
        "ConnectionRetryIntervalSeconds": 5,
        "GrpcPort": 7089,
        "SealUnbackedContentHashLists": false,
        "ConnectionsPerSession": 46,
        "DisableContent": true
    },
    "Type": "BuildXL.Cache.VerticalAggregator.VerticalCacheAggregatorFactory",
    "Assembly": "BuildXL.Cache.VerticalAggregator",
    "RemoteContentIsReadOnly": true
}
EOF
    echo $cacheConfigFile
}

function startContentStoreApp {
    local settingsFile=$(createContentStoreAppSettingsFile)
    print_info "Generated ContentStoreApp settings file: ${settingsFile}"

    local casaasArgs=(
        DistributedService
        /dataRootPath:${CACHE_ROOT}/dataRootPath
        /grpcPort:7089
        /cacheName:${CACHE_NAME}
        /cachePath:${CACHE_ROOT}/cache
        /ringId:ring-${CACHE_NAME}
        /stampId:stamp-${CACHE_NAME}
        /scenario:${CACHE_SCENARIO}
        /useDistributedGrpc:true
        /settingsPath:${settingsFile}
        /remoteTelemetry
        /logAutoFlush
        /logdirectorypath:${CACHE_ROOT}/logs)

    pushd "${CACHE_ROOT}" > /dev/null
    ${BUILDXL_BIN}/ContentStoreApp "${casaasArgs[@]}" 2>&1 > "${CACHE_STDOUT}" &
    popd > /dev/null
}

function runBuildXL {
    local cacheConfigFile=$(createCacheConfigJson)
    print_info "Generated BuildXL cache config file: ${cacheConfigFile}"

    pushd "${CACHE_ROOT}" > /dev/null
    ${MY_DIR}/build.sh --cache-config-file "${cacheConfigFile}" "$@"
    popd > /dev/null
}

function checkEnvVarExists { # (varName)
    local varName="$1"
    print_info "Checking env var '${varName}'"
    if [[ -z ${!varName} ]]; then
        print_error "Env var '${varName}' not defined"
        return 1
    fi
}

function validateAndInit {
    checkEnvVarExists "CloudStoreRedisConnectionString"
    checkEnvVarExists "VSTSPERSONALACCESSTOKEN"
    checkEnvVarExists "BUILDXL_BIN"

    if [[ ! -f $BUILDXL_BIN/ContentStoreApp ]]; then
        print_error "$BUILDXL_BIN/ContentStoreApp not found"
        return 1
    fi

    if [[ ! -d "${CACHE_ROOT}" ]]; then
        mkdir -p "${CACHE_ROOT}"
    fi
}

validateAndInit

set -o nounset 
set -o errexit

# kill any currently running ContentStoreApp proces
killRunningContentStoreApp

# start ContentStoreApp and remember its PID
startContentStoreApp

# verify that ContentStoreApp process is running and that its PID matches what we have
sleep 2
readonly casaasPid=$(printRunningCasaasPid)
if [[ -n $casaasPid ]]; then
    print_info "ContentStoreApp(${casaasPid}) process started successfully:"
    head "${CACHE_STDOUT}"
else
    print_error "Could not start ContentStoreApp"
    head "${CACHE_STDOUT}"
    exit 1
fi

# run the build
runBuildXL "$@" || print_error "Build failed."

# in any case, send SIGTERM to ContentStoreApp
print_info "Shutting down ContentStoreApp..."
kill -s TERM $casaasPid

# wait for a while until ContentStoreApp exits
for i in `seq 1 20`; do
    if [[ -z $(printRunningCasaasPid) ]]; then 
        print_info "Successfully shut down ContentStoreApp.  ContentStoreApp stdout: "
        cat "${CACHE_STDOUT}"
        exit 0
    else
        print_warning "Still running, sleeping for 1 second..."
        sleep 1
    fi
done

print_error "Failed to shut down ContentStoreApp"
exit 1