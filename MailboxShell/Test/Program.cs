using MailboxShell;
using SimpleMutithreadQueue;
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
				((Mailbox)mailbox).tick();
				Thread.Sleep(1);
			}
		}

		static void receiver(object mailbox) { 
			while(isWorking) { 
				foreach(Packet packet in ((Mailbox)mailbox).getAllReceived())
					Console.WriteLine($"Server response: \"{Encoding.UTF8.GetString(packet.data)}\"");
				Thread.Sleep(1);
			}
		}

		static void Main(string[] args) {
			
			Console.WriteLine("Select action:");
			Console.WriteLine("1. Create server and connect");
			Console.WriteLine("2. Connect to existing server");
			EchoServer echoServer = null;
			switch(Console.ReadLine()) {
				case "1":
					echoServer = new EchoServer(SERVER_IP, SERVER_PORT);
					echoServer.start();
					break;
				case "2":
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
				string request = Console.ReadLine();
				if(request.Length == 1 && request.ToLower() == "q")
					break;
				mailbox.send(new Packet(Encoding.UTF8.GetBytes(request)));
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

	public class EchoServer {

		Socket serverSocket;
		bool IsWorking { get; set; } = false;
		Thread listenConnectionsThread;
		Thread handlePacketsThread;
		Thread mailboxesTicker;

		List<Mailbox> connections = new List<Mailbox>();
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
					Console.WriteLine($"New connection: {((IPEndPoint)mailbox.Socket.RemoteEndPoint).Address.ToString()}:{((IPEndPoint)mailbox.Socket.RemoteEndPoint).Port}");
					lock(connections) {
						connections.Add(mailbox);
					}
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
						if(!mailbox.tick()) { 
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
					foreach(Mailbox mailbox in connections) { 
						foreach(Packet packet in mailbox.getAllReceived()) {
							handlePacket(mailbox, packet);
						}
					}

					foreach(Mailbox mailbox in removeQueue) { 
						connections.Remove(mailbox);
						mailbox.Socket.Close();
					}
				}
				removeQueue.R_Swap();
				Thread.Sleep(1);
			}
		}

		private void handlePacket(Mailbox senderMailbox, Packet packet) { 
			string str = Encoding.UTF8.GetString(packet.data);
			senderMailbox.send(new Packet(Encoding.UTF8.GetBytes("Echo: " + str)));
		}

	}
}
