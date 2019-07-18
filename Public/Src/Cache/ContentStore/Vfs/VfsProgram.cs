// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.ToolSupport;

namespace BuildXL.Cache.ContentStore.Vfs
{
    using System.Diagnostics.ContractsLight;
    using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
    using static CommandLineUtilities;

    internal class VfsProgram : ToolProgram<VfsCasConfiguration>
    {
        public VfsProgram()
            : base("Bvfs")
        {
        }

        public static int Main(string[] arguments)
        {
            try
            {
                return new VfsProgram().MainHandler(arguments);
            }
            catch (InvalidArgumentException e)
            {
                Console.Error.WriteLine("Execution error: " + (e.InnerException ?? e).Message);
            }

            return -1;
        }

        public override int Run(VfsCasConfiguration arguments)
        {
            var runner = new VfsCasRunner();
            runner.RunAsync(arguments).GetAwaiter().GetResult();
            return 0;
        }

        public override bool TryParse(string[] rawArgs, out VfsCasConfiguration arguments)
        {
            var cli = new CommandLineUtilities(rawArgs);
            var config = new VfsCasConfiguration.Builder();

            Dictionary<string, OptionHandler> handlers = new OptionHandler[]
            {
                new OptionHandler(new[] { "serverPort", "sp" }, opt => config.ServerGrpcPort = (int)ParseUInt32Option(opt, 0, ushort.MaxValue)),
                new OptionHandler(new[] { "backingPort", "bp" }, opt => config.BackingGrpcPort = (int)ParseUInt32Option(opt, 0, ushort.MaxValue)),
                new OptionHandler(new[] { "root" }, opt => config.RootPath = new AbsolutePath(ParsePathOption(opt))),
                new OptionHandler(new[] { "cacheName" }, opt => config.CacheName = ParseStringOption(opt)),
                new OptionHandler(new[] { "scenario" }, opt => config.Scenario = ParseStringOption(opt), required: false),
                new OptionHandler(new[] { "virtualizationMount", "vm" }, opt =>
                {
                    var kvp = ParseKeyValuePair(opt);
                    config.VirtualizationMounts[kvp.Key] = new AbsolutePath(GetFullPath(kvp.Value, opt));
                },
                required: false),
            }
            .SelectMany(handler => handler.Names.Select(name => (name, handler)))
            .ToDictionary(t => t.name, t => t.handler, StringComparer.OrdinalIgnoreCase);

            foreach (var opt in cli.Options)
            {
                if (opt.Name == "?" || opt.Name == "help")
                {
                    // TODO: Help text
                }

                if (handlers.TryGetValue(opt.Name, out var handler))
                {
                    handler.Handle(opt);
                    handler.Occurrences++;
                }
                else
                {
                    throw new InvalidArgumentException($"Unrecognized option {opt.Name}");
                }
            }

            foreach (var handler in handlers.Values.Where(h => h.Occurrences == 0 && h.Required))
            {
                throw new InvalidArgumentException($"Option '{handler.Names[0]}' is required.");
            }

            arguments = config.Build();
            return true;
        }

        private class OptionHandler
        {
            public string[] Names { get; }
            public Action<Option> Handle { get; }
            public bool Required { get; }
            public int Occurrences { get; set; }

            public OptionHandler(string[] names, Action<Option> handle, bool required = true)
            {
                Contract.Requires(names.Length != 0);
                Names = names;
                Handle = handle;
                Required = required;
            }
        }
    }
}
