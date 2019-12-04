// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <IOKit/IOKitLib.h>
#include <IOKit/IODataQueueClient.h>
#include <IOKit/kext/KextManager.h>

#include <iomanip>
#include <vector>
#include <array>
#include <iterator>
#include <string>
#include <sstream>
#include <ncurses.h>

#import "args.hpp"
#import "ps.hpp"
#import "lambda.hpp"
#import "render.hpp"
#import "Common.h"
#import "KextSandbox.h"

using namespace std;

GEN_CONFIG_DEF(ALL_ARGS)

string renderClientId(pid_t clientId)
{
    stringstream str;
    str << ps(clientId, "ucomm=") << ":" << clientId;
    return str.str();
}

string renderPipId(pipid_t pipId)
{
    stringstream str;
    str << hex << uppercase << pipId;
    return str.str();
}

typedef struct {
    string client;
    PipInfo pip;
    ProcessInfo proc;
    string procInfo;
} Tuple;

#define to_getter(x) [](Tuple t) { return to_string(x); }
#define DIV(a, b) (((b) == 0) ? 0 : ((a) / (b)))
#define DIV2(a, b) (((b) == 0) ? 0.00 : ((a) / (1.0 * (b))))
#define PERCENT(a, b) DIV2(a * 100, a + b)

string renderDouble(double d, int precision = 2)
{
    stringstream str;
    str << fixed << setprecision(precision) << d;
    return str.str();
}

string renderCounterMicros(DurationCounter cnt)
{
    return renderDouble(DIV2(cnt.duration().micros(), cnt.count()));
}

string renderCounter(DurationCounter cnt)
{
    stringstream str;
    str << renderCounterMicros(cnt) << "us";
    return str.str();
}

static const uint BytesInAMegabyte = 1 << 20;

string renderBytesAsMebabytes(double bytes)
{
    stringstream str;
    str << renderDouble(bytes/BytesInAMegabyte) << " MB";
    return str.str();
}

string renderCountAndSize(CountAndSize cnt)
{
    stringstream str;
    str << to_string(cnt.count)
        << " (" << renderBytesAsMebabytes(cnt.size * cnt.count) << ")";
    return str.str();
}

string to_string(Counter cnt)         { return to_string(cnt.count()); }
string to_string(DurationCounter cnt) { return renderCounterMicros(cnt); }
string to_string(string str)          { return str; }

vector<vector<HeaderColumn<Tuple>>> getStackedHeaders(const Config &cfg)
{
    return vector<vector<HeaderColumn<Tuple>>>(
    {
        vector<HeaderColumn<Tuple>>(
        {
            {  15, "Client",  to_getter(t.client) },
        }),
        vector<HeaderColumn<Tuple>>(
        {
            {  18, "PipId",   to_getter(renderPipId(t.pip.pipId)) },
            {   7, "PipPID",  to_getter(t.pip.pid) },
            {   6, "#Proc",   to_getter(t.pip.treeSize) },
            {   6, "#Forks",  to_getter(t.pip.counters.numForks) },
            {   8, "#C+",     to_getter(t.pip.counters.numCacheHits) },
            {   8, "#C-",     to_getter(t.pip.counters.numCacheMisses) },
            {   8, "#C",      to_getter(t.pip.cacheSize) },
            {   4, "C%",      to_getter((int)floor(PERCENT(t.pip.counters.numCacheHits.count(), t.pip.counters.numCacheMisses.count()))) },
            {   8, "avg(FP)", to_getter(t.pip.counters.findTrackedProcess) },
            {   8, "avg(SP)", to_getter(t.pip.counters.setLastLookedUpPath) },
            {   8, "avg(PC)", to_getter(t.pip.counters.checkPolicy) },
            {   8, "avg(CL)", to_getter(t.pip.counters.cacheLookup) },
            {   8, "avg(GC)", to_getter(t.pip.counters.getClientInfo) },
            {   8, "avg(RF)", to_getter(t.pip.counters.reportFileAccess) },
            {   8, "avg(AH)", to_getter(t.pip.counters.accessHandler) }
        }),
        vector<HeaderColumn<Tuple>>(
        {
            {   7, "PID",                  to_getter(t.proc.pid) },
            {   0, "(" + cfg.ps_fmt + ")", to_getter(t.procInfo) }
        }),
    });
}

static vector<PipInfo> GetPips(const IntrospectResponse *response)
{
    return vector<PipInfo>(response->pips, response->pips + response->numReportedPips);
}

static vector<ProcessInfo> GetPipChildren(const PipInfo &pip)
{
    auto vec = vector<ProcessInfo>(pip.children, pip.children + pip.numReportedChildren);
    // make sure the root process goes first
    sort(vec.begin(), vec.end(), [&pip](ProcessInfo p1, ProcessInfo p2)
         {
             if (p1.pid == pip.pid) return true;  // p1 must go before p2
             if (p2.pid == pip.pid) return false; // p2 must go before p1
             return p1.pid < p2.pid;              // don't care, so sort by pid number
         });
    return vec;
}

static void clrscr()
{
    cout << "\033[2J\033[1;1H";
}

static bool g_interrupted = false;

void signalHandler(int signum)
{
    if (signum == SIGINT)
    {
        cerr << endl << "SIGINT detected, quitting..." << endl;
        g_interrupted = true;
    }
}

void renderProcesses(const Config *cfg, const Renderer<Tuple> *renderer, const IntrospectResponse *response, stringstream *output)
{
    // group by clients
    function<pid_t(PipInfo)> select_clientId = [](PipInfo p) { return p.clientPid; };
    vector<PipInfo> pips = GetPips(response);
    map<pid_t, vector<PipInfo>> client2proc = group_by(&pips, select_clientId);
    
    // render processes
    bool newClient, newPip;
    for (auto iClient = client2proc.begin(); iClient != client2proc.end(); ++iClient)
    {
        newClient = true;
        string clientName = renderClientId(iClient->first);
        auto pips = iClient->second;
        for (auto iPip = pips.begin(); iPip != pips.end(); ++iPip)
        {
            newPip = true;
            auto procs = GetPipChildren(*iPip);
            for (auto iProc = procs.begin(); iProc != procs.end(); ++iProc)
            {
                string procInfo = ps(iProc->pid, cfg->ps_fmt);
                if (procInfo.size() == 0 && iProc != procs.begin()) continue;
                int fromHeaderIndex = newClient ? 0 : newPip ? 1 : 2;
                *output << renderer->RenderTuple(fromHeaderIndex, {clientName, *iPip, *iProc, procInfo}) << endl;
                newClient = newPip = false;
            }
        }
    }
}

void printValidPsKeywords()
{
    cout << "Valid keywords: ";
    bool first = true;
    for (auto it = ps_keywords.begin(); it != ps_keywords.end(); ++it)
    {
        if (!first) cout << ", ";
        cout << *it;
        first = false;
    }
    cout << "." << endl;
}

bool sanitizePsFormat(Config *cfg)
{
    const string delimiter = ",";
    stringstream result;
    string str = cfg->ps_fmt;
    size_t pos;
    do
    {
        pos = str.find(delimiter);

        string token;
        if (pos == string::npos)
        {
            token = str;
        }
        else
        {
            token = str.substr(0, pos);
            str.erase(0, pos + delimiter.length());
        }

        if (ps_keywords.find(token) == ps_keywords.end())
        {
            cout << "Invalid PS keyword: '" << token << "'." << endl;
            printValidPsKeywords();
            return false;
        }

        if (result.tellp() > 0)
            result << ",";
        result << token << "=";
    } while (pos != string::npos);

    cfg->ps_fmt = result.str();
    return true;
}

int main(int argc, const char * argv[])
{
    signal(SIGINT, signalHandler);
    SetLogger(nullptr);
    
    ConfigureArgs();

    Config cfg;
    if (!cfg.parse(argc, argv))
    {
        printf("\nUsage:\n\n");
        cfg.printUsage();
        exit(1);
    }
    
    if (cfg.help)
    {
        cfg.printUsage();
        exit(0);
    }
    
    if (!sanitizePsFormat(&cfg))
    {
        exit(1);
    }

    KextConnectionInfo info;
    InitializeKextConnection(&info, sizeof(info));
    
    if (info.error != 0)
    {
        error("Failed to connect to kernel extension.  Error code: %d", info.error);
        return info.error;
    }

    bool isDebug = false;
    if (!CheckForDebugMode(&isDebug, info))
    {
        error("%s", "Could not query kext for configuration mode.");
        return 1;
    }
    
    char version[10];
    KextVersionString(version, 10);
    
    auto stackedHeaders = getStackedHeaders(cfg);
    Renderer<Tuple> renderer(cfg.col_sep, &stackedHeaders, cfg.stacked);

    int loopCount = 0;
    int exitCode = 0;
    do
    {
        if (loopCount++ > 0)
        {
            sleep(cfg.delay);
        }
        
        if (g_interrupted)
        {
            break;
        }

        stringstream output;
        
        // render information about interactive mode
        if (cfg.interactive)
        {
            output << "Every " << cfg.delay << "s: ";
            for (int i = 0; i < argc; i++)
                output << argv[i] << " ";
            output << "(" << loopCount << ")" << endl;
        }

        IntrospectResponse response;
        if (!IntrospectKernelExtension(info, &response))
        {
            error("%s", "Failed to introspect sandbox kernel extension");
            exitCode = 1;
            break;
        }

        // render header
        if (!cfg.no_header)
        {
            const KextConfig *kextCfg = &response.kextConfig;
            const ResourceThresholds *thresholds = &kextCfg->resourceThresholds;
            const ResourceCounters *counters = &response.counters.resourceCounters;

            time_t rawtime;
            time(&rawtime);
            struct tm * timeinfo = localtime(&rawtime);
            char timeStr[80];
            strftime (timeStr, 80, "%Y-%m-%d %H:%M:%S", timeinfo);

            output << "[" << timeStr << "]"
                   << " Connected to sandbox version " << version
                   << " running in " << (isDebug ? "DEBUG" : "RELEASE") << " configuration"
                   << endl
                   << endl;
            output << "Config     :: "
                   << "Catalina Data Partition filtering: " << (kextCfg->enableCatalinaDataPartitionFiltering ? "YES" : "NO")
                   << ", Report Queue Size: " << kextCfg->reportQueueSizeMB << " MB"
                   << endl;
            output << "Thresholds :: "
                   << "Min Available RAM: " << thresholds->minAvailableRamMB << " MB"
                   << ", CPU usage: [" << thresholds->GetCpuUsageForWakeup().value << "..." << thresholds->cpuUsageBlock.value << "]%"
                   << endl;
            output << "Counters   :: "
                   << "Avg(FindProcess/SetLastPath/PolicyCheck/CacheLookup/GetClient/ReportFileAccess/AccessHandler): "
                   << renderCounter(response.counters.findTrackedProcess) << " / "
                   << renderCounter(response.counters.setLastLookedUpPath) << " / "
                   << renderCounter(response.counters.checkPolicy) << " / "
                   << renderCounter(response.counters.cacheLookup) << " / "
                   << renderCounter(response.counters.getClientInfo) << " / "
                   << renderCounter(response.counters.reportFileAccess) << " / "
                   << renderCounter(response.counters.accessHandler)
                   << endl;
            output << "Reports    :: "
                   << "#Queued: " << to_string(response.counters.reportCounters.numQueued)
                   << ", Total: " << to_string(response.counters.reportCounters.totalNumSent)
                   << ", #HardLink retries: " << to_string(response.counters.numHardLinkRetries)
                   << ", #CoalescedReports: " << to_string(response.counters.reportCounters.numCoalescedReports)
                   << " (" << renderDouble(PERCENT(response.counters.reportCounters.numCoalescedReports.count(), response.counters.reportCounters.totalNumSent.count())) << "%)"
                   << endl;
            output << "Memory     :: "
                   << "FastTrieNodes: " << renderCountAndSize(response.memory.fastNodes)
                   << ", LightTrieNodes: " << renderCountAndSize(response.memory.lightNodes)
                   << ", CacheRecords: " << renderCountAndSize(response.memory.cacheRecords)
                   << ", FreeListNodes: " << to_string(response.counters.reportCounters.freeListNodeCount)
                   << " (" << renderDouble(response.counters.reportCounters.freeListSizeMB) << " MB)"
                   << ", IONew allocations: " << renderBytesAsMebabytes(response.memory.totalAllocatedBytes)
                   << endl;
            output << "Processes  :: #Client: " << response.numAttachedClients
                   << ", #Pips: " << response.numReportedPips
                   << ", Available RAM: " << counters->availableRamMB << " MB"
                   << ", CPU usage: " << renderDouble(counters->cpuUsage.value / 100.0) << "%"
                   << ", #Processes [active: " << to_string(counters->numTrackedProcesses)
                   << ", blocked: " << to_string(counters->numBlockedProcesses) << "]"
                   << endl
                   << endl;
            output << renderer.RenderHeader() << endl;
        }

        // render processes
        renderProcesses(&cfg, &renderer, &response, &output);

        // print to stdout
        if (cfg.interactive)
            clrscr();
        cout << output.str();

    } while (cfg.interactive && !g_interrupted);

    DeinitializeKextConnection(info);

    return exitCode;
}
