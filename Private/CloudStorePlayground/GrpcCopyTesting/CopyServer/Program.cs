using System;
using Grpc.Core;
using Helloworld;

namespace CopyServer
{
    class Program
    {
        static void Main(string[] args)
        {
            CopierImplementation implementation = new CopierImplementation();

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
