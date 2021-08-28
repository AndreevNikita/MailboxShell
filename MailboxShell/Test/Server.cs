/* Async send/receive mode */

//#define ASYNC_SERVER

/* ----------------------- */

using MailboxShell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;


namespace Test
{
	public class ClientInfo : IMailboxOwner { 

		public MailboxSafe MailboxSafe { get; } = new MailboxSafe();

		public int messageCounter { get; set; } = 0;

		private static int CurrentClientNumber = 0; 
		public int Number { get; } = CurrentClientNumber++;
		public int ReceivedPackets { get; set; } = 0;
	}

	public class EchoServer {

		Socket serverSocket;
		bool IsWorking { get; set; } = false;
		Task listenConnectionsTask;
		Thread handlePacketsThread;
		Thread mailboxesTicker;

		List<Mailbox> connections = new List<Mailbox>();
		Channel<Mailbox> newConnections = Channel.CreateUnbounded<Mailbox>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = true});
		Channel<Mailbox> newTickConnections = Channel.CreateUnbounded<Mailbox>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = true});
		Channel<Mailbox> removeQueue = Channel.CreateUnbounded<Mailbox>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = true});

		public EchoServer(string ip, int port) {
			serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			serverSocket.Bind(new IPEndPoint(IPAddress.Parse(ip), port));
		}

		public void Start() { 
			IsWorking = true;
			handlePacketsThread = new Thread(handlePackets);
			mailboxesTicker = new Thread(tickerFunc);
			listenConnectionsTask = listenConnections();
			handlePacketsThread.Start();
			mailboxesTicker.Start();
			Console.WriteLine($"Server started at {((IPEndPoint)serverSocket.LocalEndPoint).Address.ToString()}:{((IPEndPoint)serverSocket.LocalEndPoint).Port}");
		}

		public void Stop() { 
			serverSocket.Close();
			IsWorking = false;
			mailboxesTicker.Join();
			handlePacketsThread.Join();
			listenConnectionsTask.Wait();
			foreach(Mailbox mailbox in connections) { 
				mailbox.Socket.Close();
			}
			Console.WriteLine("Server stopped");
		}

		public async Task listenConnections() { 
			try {
				serverSocket.Listen(100);
				while(true) { 
					Mailbox mailbox = new Mailbox(await serverSocket.AcceptAsync());
#if ASYNC_SERVER
					mailbox.StartListenAsync(1000);
#endif

					//mailbox.SetOwner(new ClientInfo());
					new ClientInfo().SetMailbox(mailbox);


					Console.WriteLine($"New connection: {((IPEndPoint)mailbox.Socket.RemoteEndPoint).Address.ToString()}:{((IPEndPoint)mailbox.Socket.RemoteEndPoint).Port}; {mailbox.GetOwner<ClientInfo>().Number} ");
					await newConnections.Writer.WriteAsync(mailbox);
					await newTickConnections.Writer.WriteAsync(mailbox);
					await Task.Delay(1);
				}
			} catch(SocketException e) {
				if(e.SocketErrorCode != SocketError.OperationAborted) {
					Console.WriteLine(e.Message);
				}
			}
		}

		private List<Mailbox> tickMailboxes = new List<Mailbox>();

		public void tickerFunc() {
			while(IsWorking) {
				while(newTickConnections.Reader.TryRead(out Mailbox newMailbox)) { 
					tickMailboxes.Add(newMailbox);
				}

				List<Mailbox> removeList = new List<Mailbox>();
				foreach(Mailbox mailbox in tickMailboxes) { 
					mailbox.Send(new Packet(Encoding.UTF8.GetBytes("p"))); //A ping packet
#if !ASYNC_SERVER
					mailbox.Tick();
#endif

					
					if(!mailbox.IsConnected) {
						removeList.Add(mailbox);
						Console.WriteLine("Remove mailbox");

					}
				}

				foreach(Mailbox mailbox in removeList) { 
					tickMailboxes.Remove(mailbox);
					removeQueue.Writer.WriteAsync(mailbox);
				}
				Thread.Sleep(1);
			}
		}

		public void handlePackets() { 
			
			while(IsWorking) { 
				while(newConnections.Reader.TryRead(out Mailbox newConnection)) {
					connections.Add(newConnection);
				}

				foreach(Mailbox mailbox in connections) { 
					foreach(Packet packet in mailbox.GetAllReceived()) {
						handlePacket(mailbox, packet);
					}
				}

				while(removeQueue.Reader.TryRead(out Mailbox mailbox)) { 
					ClientInfo clientInfo = mailbox.GetOwner<ClientInfo>();
					Console.WriteLine($"Disconnected {clientInfo.Number} (Packets received: {clientInfo.ReceivedPackets})");
					connections.Remove(mailbox);
#if ASYNC_SERVER
					mailbox.StopListenAsync();
#endif
					mailbox.Socket.Close();
				}
				Thread.Sleep(1);
			}
		}

		private void handlePacket(Mailbox senderMailbox, Packet packet) { 
			senderMailbox.GetOwner<ClientInfo>().ReceivedPackets++;
			string str = packet.length < 1024 ? Encoding.UTF8.GetString(packet.data) : "Big packet!";
			if(str == "p") //A ping packet
				return;
			senderMailbox.Send(new Packet(Encoding.UTF8.GetBytes($"Echo ({senderMailbox.GetOwner<ClientInfo>().messageCounter++}): " + str)));
		}
	}
}
