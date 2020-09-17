using System;
using Grpc.Core;
using Helloworld;

namespace CopyServer
{
    class Program
    {
        static void Main(string[] args)
        {
            CopierImplementation implementation;
            if (args.Length > 0 && !String.IsNullOrEmpty(args[0]))
            {
                CopierImplementationMode mode = Enum.Parse<CopierImplementationMode>(args[0]);
                implementation = new CopierImplementation(mode);
            }
            else
            {
                implementation = new CopierImplementation();
            }

            ChannelOption[] options = new ChannelOption[] {  };
            Server server = new Server(options)
            {
                Services = { Copier.BindService(implementation) },
                Ports = { new ServerPort("localhost", CopyConstants.PortNumber, ServerCredentials.Insecure)}
            };

            server.Start();

            Console.WriteLine("Listening");
            Console.ReadLine();
        }
    }
}
