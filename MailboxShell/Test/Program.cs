using MailboxShell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Test {
	class Program {

		const string SERVER_IP = "0.0.0.0";
		const int SERVER_PORT = 2020;

		const string CONNECT_IP = "127.0.0.1";
		//const string CONNECT_IP = "192.168.1.3";
		const int CONNECT_PORT = 2020;

		static void Main(string[] args) {
			
			Console.WriteLine("Select action:");
			Console.WriteLine("1. Create server and connect");
			Console.WriteLine("2. Connect to existing server");
			Console.WriteLine("3. Stress test an exisiting server");
			EchoServer echoServer = null;
			bool stressTest = false;
			switch(Console.ReadLine()) {
				case "1":
					echoServer = new EchoServer(SERVER_IP, SERVER_PORT);
					echoServer.Start();
					Console.WriteLine("Enter:\n" +
						"'b' to send a big packet\n" +
						"'h' to send incorrect packet\n" +
						"'q' to exit;");
					break;
				case "2":
					break;
				case "3":
					stressTest = true;
					break;
			}
			
			
			if(stressTest) {
				Console.Write("Enter the number of clients: ");
				CancellationTokenSource clientsCTS = new CancellationTokenSource();
				int clientsCount = int.Parse(Console.ReadLine());
				List<Client> clients = new List<Client>();
				List<Task> clientsTasks = new List<Task>();
				for(int counter = 0; counter < clientsCount; counter++) {
					Client client = new Client(CONNECT_IP, CONNECT_PORT);
					clients.Add(client);
					clientsTasks.Add(client.StartAsync(clientsCTS.Token, true));
				}

				Task.WaitAll(clientsTasks.ToArray());
			} else {
				Client client = new Client(CONNECT_IP, CONNECT_PORT);
				CancellationTokenSource clientCTS = new CancellationTokenSource();
				Task clientTask = client.StartAsync(clientCTS.Token, false);

				while(true) { 
					string request = Console.ReadLine();
					string lowerRequest = request.ToLower();
					if(!client.Mailbox.IsConnected) {
						break;
					}

					if(lowerRequest == "q") { 
						break;
					} else if(lowerRequest == "b") { 
						Packet packet = new Packet(new byte[100500]);
						client.Mailbox.Send(packet);
						continue;
					} else if(lowerRequest == "h") { 
						Packet packet = new Packet(new byte[0]);
						packet.length = -5;
						client.Mailbox.Send(packet);
						continue;
					} else { 
						Packet packet = new Packet(Encoding.UTF8.GetBytes(request));
						client.Mailbox.Send(packet);
					}
				}

				Console.WriteLine("Wait to send all and stop");
				client.Mailbox.StopListen(true);
				clientCTS.Cancel();
				clientTask.Wait();
				Console.WriteLine($"Packets waiting to be sent: {client.Mailbox.SendQueueSize}");
			}

			if(echoServer != null) {
				Console.WriteLine("Press any key to close the server");
				Console.ReadKey();
				echoServer.Stop();
				Console.ReadKey();
			}
		}
	}

	
}
