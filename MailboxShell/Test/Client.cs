using MailboxShell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Test
{
	public class Client {

		public Mailbox Mailbox { get; private set; }
		

		public Client(string connectIp, int connectPort) { 
			Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			socket.Connect(IPAddress.Parse(connectIp), connectPort);
			Mailbox = new Mailbox(socket);
		}

		private async Task ticker(CancellationToken cancellationToken, CancellationTokenSource selfCTS) {
			while(!cancellationToken.IsCancellationRequested) { 
				Mailbox.Send(new Packet(Encoding.UTF8.GetBytes("p"))); //A ping packet
				//SendedPackets++;
				if(!Mailbox.IsConnected) {
					Console.WriteLine("Server closed");
					selfCTS.Cancel();
					break;
				}

				try {
					await Task.Delay(1, cancellationToken);
				} catch(TaskCanceledException) { 
					break;
				}
			}
			Console.WriteLine("Client's ticker stopped");
		}

		private async Task receiver(CancellationToken cancellationToken) { 
			while(!cancellationToken.IsCancellationRequested && Mailbox.IsConnected) {
				Packet packet;
				try {
					packet = await Mailbox.GetNextAsync(cancellationToken);
				} catch(OperationCanceledException) { 
					break;
				}
				string responseString = Encoding.UTF8.GetString(packet.data);
				if(responseString == "p")
					continue;
				Console.WriteLine($"Server response: \"{responseString}\"");
			}
			Console.WriteLine("Client's packets handler stopped");
		}

		private async Task trafficEmulator(CancellationToken cancellationToken) { 
			Random rand = new Random();
			while(!cancellationToken.IsCancellationRequested && Mailbox.IsConnected) { 
				Mailbox.Send(new Packet(rand.NextDouble() < 0.90 ? Encoding.UTF8.GetBytes("!!!!!!STRESS_TEST!!!!!!") : new byte[100500]));
				try {
					await Task.Delay(1, cancellationToken);
				} catch(TaskCanceledException) { 
					break;
				}
			}
		}

		public async Task StartAsync(CancellationToken cancellationToken, bool emulateTraffic = false) { 
			CancellationTokenSource selfCTS = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			List<Task> waitTasks = new List<Task> { 
				Mailbox.StartListenAsync(),
				ticker(cancellationToken, selfCTS), receiver(selfCTS.Token)
			};
			if(emulateTraffic)
				waitTasks.Add(trafficEmulator(selfCTS.Token));
			try {
			await Task.WhenAll(waitTasks);
			} catch(Exception e) { 
				Console.WriteLine($"When all exception {e.Message}");
			}
		}
	}
}
