using SimpleMultithreadQueue;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace MailboxShell
{
	public class Packet {
		public int length;
		public int handledLength = -4; //Отрицательное значение означает, что принимается или передаётся длина пакета данных
		public byte[] data;

		public Packet(int length = 0) {
			this.length = length;
			this.handledLength = -4;
			if(length > 0)
				createBuffer();
		}

		public Packet(byte[] data) {
			this.data = data;
			this.length = data.Length;
			this.handledLength = -4;
		}

		internal void createBuffer() { 
			data = new byte[length];
		}

		public bool IsLengthKnown {
			get { 
				return handledLength >= 0;
			}
		}
	}

	public class Mailbox {
		public Socket Socket { get; private set; }
		readonly int maxPacketSize;
		MultithreadQueue<Packet> sendQueue = new MultithreadQueue<Packet>();
		MultithreadQueue<Packet> receivedQueue = new MultithreadQueue<Packet>();

		Packet currentSendPacket = null;
		Packet currentReceivePacket = null;
		int packetsPerTick;

		public Mailbox(Socket socket, int packetsPerTick = 0, int maxPacketSize = 0) {
			this.Socket = socket;
			socket.Blocking = false; //Перевод сокета в неблокируемый режим
			this.packetsPerTick = packetsPerTick;
			this.maxPacketSize = maxPacketSize;
		}

		public void send(Packet packet) {
			sendQueue.Enqueue(packet);
		}

		public Packet next() {
			Packet packet;
			if(receivedQueue.R_Dequeue(out packet))
				return packet;
			return null;
		}

		public IEnumerable<Packet> swapGetReceived() { 
			return receivedQueue.R_PopReadyToNewQueue(true);
		}

		public IEnumerable<Packet> getAllReceived() { 
			return receivedQueue.R_PopAll();
		}

		private byte[] receiveLengthBuffer = new byte[sizeof(int)];

		public virtual bool tick() {
			int packetsCounter;

			if(!Socket.Connected)
				return false;
			try {
				packetsCounter = 0;
				while(true) {
					if(currentReceivePacket == null)
						currentReceivePacket = new Packet();

					if(!currentReceivePacket.IsLengthKnown) { 
						if(Socket.Available >= receiveLengthBuffer.Length) {
							currentReceivePacket.handledLength += Socket.Receive(receiveLengthBuffer, 0, receiveLengthBuffer.Length, 0);
							currentReceivePacket.length = BitConverter.ToInt32(receiveLengthBuffer, 0);
						
							if(!currentReceivePacket.IsLengthKnown)
								break;
							currentReceivePacket.createBuffer();
						} else
							break;
					} 

					if(currentReceivePacket.IsLengthKnown) {
						if(Socket.Available >= currentReceivePacket.length) { 
							if(currentReceivePacket.length == 0) {
							} else if(maxPacketSize != 0 && currentReceivePacket.length > maxPacketSize) { 
								Socket.Close();
								return false;
							} else {
								try {
									currentReceivePacket.handledLength += Socket.Receive(currentReceivePacket.data, currentReceivePacket.handledLength, currentReceivePacket.length - currentReceivePacket.handledLength, 0);
								} catch(Exception e) { 
									Console.WriteLine(e);
									Console.WriteLine(e.StackTrace);
								}
							}

							if(currentReceivePacket.handledLength == currentReceivePacket.length) { 
								//lock(receivedQueue)
								receivedQueue.Enqueue(currentReceivePacket);
								currentReceivePacket = null;
								packetsCounter++;
								if(packetsPerTick != 0 && packetsCounter == packetsPerTick)
									break;
							} else
								break;
						} else
							break;
					}
				}
			
				packetsCounter = 0;
				while(true) {
					
					if(currentSendPacket == null) {
						if(!sendQueue.R_DequeueReady(out currentSendPacket)) {
							break;
						}
					}

					if(!currentSendPacket.IsLengthKnown) { //Если длина пакета данных передалась не полностью
						byte[] lengthBytes = BitConverter.GetBytes(currentSendPacket.length);
						currentSendPacket.handledLength += Socket.Send(lengthBytes, 4 + currentSendPacket.handledLength, -currentSendPacket.handledLength, 0);
					} 

					if(currentSendPacket.IsLengthKnown) { //Если длина пакета уже передалась
						currentSendPacket.handledLength += Socket.Send(currentSendPacket.data, currentSendPacket.handledLength, currentSendPacket.length - currentSendPacket.handledLength, 0);
						if(currentSendPacket.handledLength == currentSendPacket.length) {
							currentSendPacket = null;
							packetsCounter++;
							if(packetsPerTick != 0 && packetsCounter == packetsPerTick)
								break;
						}  else
							break;
					}
				}
			} catch(SocketException e) {
				Console.WriteLine(e.Message);
				Console.WriteLine(e.StackTrace);
				return false;
			}
			return true;
		}
	}
}
