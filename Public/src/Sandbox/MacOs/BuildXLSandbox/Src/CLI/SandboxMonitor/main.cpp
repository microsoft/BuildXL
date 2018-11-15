//
//  main.cpp
//  SandboxMonitor
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

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
#import "Sandbox.h"

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
} Tuple;

string to_string(string str) { return str; }
#define to_getter(x) [](Tuple t) { return to_string(x); }

vector<vector<HeaderColumn<Tuple>>> getStackedHeaders(const Config &cfg)
{
    return vector<vector<HeaderColumn<Tuple>>>(
    {
        vector<HeaderColumn<Tuple>>(
        {
            {  20, "Client",  to_getter(t.client) },
        }),
        vector<HeaderColumn<Tuple>>(
        {
            {  18, "PipId",   to_getter(renderPipId(t.pip.pipId)) },
            {   7, "PipPID",  to_getter(t.pip.pid) },
            {   6, "#Proc",   to_getter(t.pip.treeSize) },
            {   8, "#C+",     to_getter(t.pip.numCacheHits) },
            {   8, "#C-",     to_getter(t.pip.numCacheMisses) }
        }),
        vector<HeaderColumn<Tuple>>(
        {
            {   8, "PID",     to_getter(t.proc.pid) },
            {
                0,
                "(" + cfg.ps_fmt + ")",
                [cfg](Tuple t) { return ps(t.proc.pid, cfg.ps_fmt); }
            }
        }),
    });
}

static vector<PipInfo> GetPips(const IntrospectResponse *response)
{
    return vector<PipInfo>(response->pips, response->pips + response->numReportedPips);
}

static vector<ProcessInfo> GetPipChildren(const PipInfo &pip)
{
    return vector<ProcessInfo>(pip.children, pip.children + pip.numReportedChildren);
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

void renderProcesses(const Renderer<Tuple> *renderer, const IntrospectResponse *response, stringstream *output)
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
                int fromHeaderIndex = newClient ? 0 : newPip ? 1 : 2;
                *output << renderer->RenderTuple(fromHeaderIndex, {clientName, *iPip, *iProc}) << endl;
                newClient = newPip = false;
            }
        }
    }
}

int main(int argc, const char * argv[]) {
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
    
    KextConnectionInfo info;
    InitializeKextConnection(&info);
    
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
            output << "Connected to sandbox version " << version
                   << " running in " << (isDebug ? "DEBUG" : "RELEASE") << " configuration"
                   << endl;
            output << "Num Client: " << response.numAttachedClients
                   << ", Num Pips: " << response.numReportedPips
                   << ", Num Processes: " << response.numTrackedProcesses
                   << endl;
            output << renderer.RenderHeader() << endl;
        }
        
        // render processes
        renderProcesses(&renderer, &response, &output);

        // print to stdout
        if (cfg.interactive)
            clrscr();
        cout << output.str();

    } while (cfg.interactive && !g_interrupted);

    DeinitializeKextConnection(info);

    return exitCode;
}
