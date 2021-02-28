using MailboxShell;
using SimpleMultithreadQueue;
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

		const string SERVER_IP = "127.0.0.1";
		const int SERVER_PORT = 2020;

		static bool isWorking = false;

		static void ticker(object mailbox) {
			while(isWorking) { 
				((Mailbox)mailbox).Send(new Packet(Encoding.UTF8.GetBytes("p")));
				((Mailbox)mailbox).Tick();
				if(!((Mailbox)mailbox).IsConnected) {
					Console.WriteLine("Server closed");
					break;
				}
				Thread.Sleep(1);
			}
		}

		static void receiver(object mailbox) { 
			while(isWorking) { 
				foreach(Packet packet in ((Mailbox)mailbox).ForeachReceived()) {
					string responseString = Encoding.UTF8.GetString(packet.data);
					if(responseString != "p")	
						Console.WriteLine($"Server response: \"{responseString}\"");
				}
				Thread.Sleep(1);
			}
		}

		static void Main(string[] args) {
			Console.WriteLine("Select action:");
			Console.WriteLine("1. Create server and connect");
			Console.WriteLine("2. Connect to existing server");
			Console.WriteLine("3. Stress test exisiting server");
			EchoServer echoServer = null;
			bool stressTest = false;
			switch(Console.ReadLine()) {
				case "1":
					echoServer = new EchoServer(SERVER_IP, SERVER_PORT);
					echoServer.start();
					break;
				case "2":
					break;
				case "3":
					stressTest = true;
					break;
			}
			

			Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			socket.Connect(IPAddress.Parse(SERVER_IP), SERVER_PORT);
			Mailbox mailbox = new Mailbox(socket);

			Thread mailboxTicker = new Thread(ticker);
			Thread mailboxReceiver = new Thread(receiver);
			isWorking = true;
			mailboxTicker.Start(mailbox);
			mailboxReceiver.Start(mailbox);
			Console.WriteLine("Enter q to exit");
			while(true) { 
				string request = !stressTest ? Console.ReadLine() : "!!!!!!STRESS_TEST!!!!!!";
				if(request.Length == 1 && request.ToLower() == "q")
					break;

				if(!mailbox.IsConnected)
						break;

				mailbox.Send(new Packet(Encoding.UTF8.GetBytes(request)));
				
				
				Thread.Sleep(1);
			}
			isWorking = false;
			mailboxTicker.Join();
			mailboxReceiver.Join();
			socket.Close();
			Console.WriteLine("Client stopped");

			echoServer?.stop();
			Console.ReadKey();
		}
	}

	public class ClientInfo : IMailboxOwner { 

		public MailboxSafe MailboxSafe { get; } = new MailboxSafe();

		public int messageCounter { get; set; } = 0;

		private static int CurrentClientNumber = 0; 
		public int Number { get; } = CurrentClientNumber++;
	}

	public class EchoServer {

		Socket serverSocket;
		bool IsWorking { get; set; } = false;
		Thread listenConnectionsThread;
		Thread handlePacketsThread;
		Thread mailboxesTicker;

		List<Mailbox> connections = new List<Mailbox>();
		MultithreadQueue<Mailbox> newConnections = new MultithreadQueue<Mailbox>();
		MultithreadQueue<Mailbox> removeQueue = new MultithreadQueue<Mailbox>();

		public EchoServer(string ip, int port) {
			serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			serverSocket.Bind(new IPEndPoint(IPAddress.Parse(ip), port));
		}

		public void start() { 
			IsWorking = true;
			listenConnectionsThread = new Thread(listenConnections);
			handlePacketsThread = new Thread(handlePackets);
			mailboxesTicker = new Thread(tickerFunc);
			listenConnectionsThread.Start();
			handlePacketsThread.Start();
			mailboxesTicker.Start();
			Console.WriteLine($"Server started at {((IPEndPoint)serverSocket.LocalEndPoint).Address.ToString()}:{((IPEndPoint)serverSocket.LocalEndPoint).Port}");
		}

		public void stop() { 
			serverSocket.Close();
			IsWorking = false;
			mailboxesTicker.Join();
			handlePacketsThread.Join();
			foreach(Mailbox mailbox in connections) { 
				mailbox.Socket.Close();
			}
			Console.WriteLine("Server stopped");
		}

		public void listenConnections() { 
			try {
				serverSocket.Listen(10);
				while(true) { 
					Mailbox mailbox = new Mailbox(serverSocket.Accept());
					
					//mailbox.SetOwner(new ClientInfo());
					new ClientInfo().SetMailbox(mailbox);


					Console.WriteLine($"New connection: {((IPEndPoint)mailbox.Socket.RemoteEndPoint).Address.ToString()}:{((IPEndPoint)mailbox.Socket.RemoteEndPoint).Port}; {mailbox.GetOwner<ClientInfo>().Number} ");
					newConnections.Enqueue(mailbox);
					Thread.Sleep(1);
				}
			} catch(SocketException e) {
				Console.WriteLine(e.Message);
			}
		}

		public void tickerFunc() {
			while(IsWorking) {
				lock(connections) {
					foreach(Mailbox mailbox in connections) { 
						mailbox.Send(new Packet(Encoding.UTF8.GetBytes("p")));

						mailbox.Tick();
						if(!mailbox.IsConnected) { 
							removeQueue.Enqueue(mailbox);
						}
					}
				}
				Thread.Sleep(1);
			}
		}

		public void handlePackets() { 
			
			while(IsWorking) { 
				lock(connections) {
					foreach(Mailbox newConnection in newConnections.R_PopAllToNewQueue()) {
						connections.Add(newConnection);
					}
				}

				foreach(Mailbox mailbox in connections) { 
					foreach(Packet packet in mailbox.GetAllReceived()) {
						handlePacket(mailbox, packet);
					}
				}

				lock(connections) {
					foreach(Mailbox mailbox in removeQueue.R_PopAllToNewQueue()) { 
						Console.WriteLine($"Disconnected {mailbox.GetOwner<ClientInfo>().Number} ");
						connections.Remove(mailbox);
						mailbox.Socket.Close();
					}
				}
				Thread.Sleep(1);
			}
		}

		private void handlePacket(Mailbox senderMailbox, Packet packet) { 
			string str = Encoding.UTF8.GetString(packet.data);
			if(str == "p")
				return;
			senderMailbox.Send(new Packet(Encoding.UTF8.GetBytes($"Echo ({senderMailbox.GetOwner<ClientInfo>().messageCounter++}): " + str)));
		}

	}
}
