// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VSCode.DebugProtocol;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace VSCode.DebugAdapter
{
    public abstract class RemoteClientDebugger : ProtocolServer
    {
        private readonly bool m_debuggerLinesStartAt1;
        private readonly bool m_debuggerPathsAreURI;
        private bool m_clientLinesStartAt1 = true;
        private bool m_clientPathsAreURI = true;

        protected RemoteClientDebugger(bool debuggerLinesStartAt1, bool debuggerPathsAreURI = false)
        {
            m_debuggerLinesStartAt1 = debuggerLinesStartAt1;
            m_debuggerPathsAreURI = debuggerPathsAreURI;
        }

        public abstract ISession Session { get; }

        protected override void DispatchRequest(IRequest request)
        {
            string jsonArgs = ((JObject)request.Arguments)?.ToString();
            switch (request.Command)
            {
                case "initialize":
                    var cmd = CreateCommand<InitializeCommand>(request, jsonArgs);
                    m_clientLinesStartAt1 = cmd.LinesStartAt1;
                    var pathFormat = cmd.PathFormat;
                    if (pathFormat != null)
                    {
                        switch (pathFormat)
                        {
                            case "uri":
                                m_clientPathsAreURI = true;
                                break;
                            case "path":
                                m_clientPathsAreURI = false;
                                break;
                            default:
                                return;
                        }
                    }

                    Session.Initialize(cmd);
                    break;

                case "launch":
                    Session.Launch(CreateCommand<LaunchCommand>(request, jsonArgs));
                    break;

                case "attach":
                    Session.Attach(CreateCommand<AttachCommand>(request, jsonArgs));
                    break;

                case "disconnect":
                    Session.Disconnect(CreateCommand<DisconnectCommand>(request, jsonArgs));
                    Stop();
                    break;

                case "next":
                    Session.Next(CreateCommand<NextCommand>(request, jsonArgs));
                    break;

                case "continue":
                    Session.Continue(CreateCommand<ContinueCommand>(request, jsonArgs));
                    break;

                case "stepIn":
                    Session.StepIn(CreateCommand<StepInCommand>(request, jsonArgs));
                    break;

                case "stepOut":
                    Session.StepOut(CreateCommand<StepOutCommand>(request, jsonArgs));
                    break;

                case "pause":
                    Session.Pause(CreateCommand<PauseCommand>(request, jsonArgs));
                    break;

                case "stackTrace":
                    Session.StackTrace(CreateCommand<StackTraceCommand>(request, jsonArgs));
                    break;

                case "scopes":
                    Session.Scopes(CreateCommand<ScopesCommand>(request, jsonArgs));
                    break;

                case "variables":
                    Session.Variables(CreateCommand<VariablesCommand>(request, jsonArgs));
                    break;

                case "source":
                    Session.Source(CreateCommand<SourceCommand>(request, jsonArgs));
                    break;

                case "threads":
                    Session.Threads(CreateCommand<ThreadsCommand>(request, jsonArgs));
                    break;

                case "setBreakpoints":
                    Session.SetBreakpoints(CreateCommand<SetBreakpointsCommand>(request, jsonArgs));
                    break;

                case "setFunctionBreakpoints":
                    Session.SetFunctionBreakpoints(CreateCommand<SetFunctionBreakpointsCommand>(request, jsonArgs));
                    break;

                case "setExceptionBreakpoints":
                    Session.SetExceptionBreakpoints(CreateCommand<SetExceptionBreakpointsCommand>(request, jsonArgs));
                    break;

                case "evaluate":
                    Session.Evaluate(CreateCommand<EvaluateCommand>(request, jsonArgs));
                    break;

                case "configurationDone":
                    Session.ConfigurationDone(CreateCommand<ConfigurationDoneCommand>(request, jsonArgs));
                    break;

                case "completions":
                    Session.Completions(CreateCommand<CompletionsCommand>(request, jsonArgs));
                    break;

                default:
                    break;
            }
        }

        // protected
        protected int ConvertDebuggerLineToClient(int line)
        {
            checked
            {
                return m_debuggerLinesStartAt1
                    ? m_clientLinesStartAt1 ? line : line - 1
                    : m_clientLinesStartAt1 ? line + 1 : line;
            }
        }

        protected int ConvertClientLineToDebugger(int line)
        {
            checked
            {
                return m_debuggerLinesStartAt1
                    ? m_clientLinesStartAt1 ? line : line + 1
                    : m_clientLinesStartAt1 ? line - 1 : line;
            }
        }

        protected string ConvertDebuggerPathToClient(string path)
        {
            if (m_debuggerPathsAreURI)
            {
                if (m_clientPathsAreURI)
                {
                    return path;
                }
                else
                {
                    Uri uri = new Uri(path);
                    return uri.LocalPath;
                }
            }
            else
            {
                if (m_clientPathsAreURI)
                {
                    try
                    {
                        var uri = new System.Uri(path);
                        return uri.AbsoluteUri;
                    }
#pragma warning disable ERP022 // TODO: This should really catcht he proper exceptions
                    catch
                    {
                        return null;
                    }
#pragma warning restore ERP022
                }
                else
                {
                    return path;
                }
            }
        }

        protected string ConvertClientPathToDebugger(string clientPath)
        {
            if (clientPath == null)
            {
                return null;
            }

            if (m_debuggerPathsAreURI)
            {
                if (m_clientPathsAreURI)
                {
                    return clientPath;
                }
                else
                {
                    var uri = new System.Uri(clientPath);
                    return uri.AbsoluteUri;
                }
            }
            else
            {
                if (m_clientPathsAreURI)
                {
                    if (Uri.IsWellFormedUriString(clientPath, UriKind.Absolute))
                    {
                        Uri uri = new Uri(clientPath);
                        return uri.LocalPath;
                    }

                    return null;
                }
                else
                {
                    return clientPath;
                }
            }
        }

        protected T CreateCommand<T>(IRequest request, string jsonArgs)
        {
            var ans = JsonConvert.DeserializeObject<T>(jsonArgs ?? "{}");
            var ansAsCommand = ans as CommandBase;
            ansAsCommand.SendResponse = new Action<bool, object>((success, body) =>
                SendMessage(new Response(request.Seq, request.Command, success: success, body: body)));
            return ans;
        }

        protected void SendErrorResponse(IRequest request, string message)
        {
            var body = new ErrorResult(new Message(-1, message));
            SendMessage(new Response(request.Seq, request.Command, success: false, body: body));
        }
    }
}
