using SimpleMutithreadQueue;
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
		public readonly Socket socket;
		readonly int maxPacketSize;
		//Queue<Packet> sendQueue = new Queue<Packet>();
		MultithreadQueue<Packet> sendQueue = new MultithreadQueue<Packet>();
		MultithreadQueue<Packet> receivedQueue = new MultithreadQueue<Packet>();

		Packet currentSendPacket = null;
		Packet currentReceivePacket = null;

		public Mailbox(Socket socket, int maxPacketSize = 1024) {
			this.socket = socket;
			socket.Blocking = false; //Перевод сокета в неблокируемый режим
			this.maxPacketSize = maxPacketSize;
		}

		public void send(Packet packet) {
			sendQueue.Enqueue(packet);
		}

		public void send(params object[] data) {
			send(new SendPacket().writeArray(data).flush());
		}

		public Packet next() {
			Packet packet;
			if(receivedQueue.R_Dequeue(out packet))
				return packet;
			return null;
		}

		public virtual bool tick() {
			if(!socket.Connected)
				return false;
			try {
				while(true) {
					if(currentReceivePacket == null)
						currentReceivePacket = new Packet();

					if(!currentReceivePacket.IsLengthKnown) { 
						if(socket.Available >= -currentReceivePacket.handledLength) {
							byte[] buffer = new byte[-currentReceivePacket.handledLength];
							currentReceivePacket.handledLength += socket.Receive(buffer, 0, -currentReceivePacket.handledLength, 0);
							currentReceivePacket.length |= BitConverter.ToInt32(buffer, 0) << ((4 - buffer.Length) * 8);
						
							if(!currentReceivePacket.IsLengthKnown)
								break;
							currentReceivePacket.createBuffer();
						} else
							break;
					} else {
						if(socket.Available >= currentReceivePacket.length) { 
							try {
								currentReceivePacket.handledLength += socket.Receive(currentReceivePacket.data, currentReceivePacket.handledLength, currentReceivePacket.length - currentReceivePacket.handledLength, 0);
							} catch(Exception e) { 
								Console.WriteLine(e);
							}
							if(currentReceivePacket.handledLength == currentReceivePacket.length) { 
								//lock(receivedQueue)
								receivedQueue.Enqueue(currentReceivePacket);
								currentReceivePacket = null;
							} else
								break;
						} else
							break;
					}
				}
			
				
				while(true) {
					
					if(currentSendPacket == null) {
						if(!sendQueue.R_DequeueReady(out currentSendPacket)) {
							break;
						}
					}

					if(!currentSendPacket.IsLengthKnown) { //Если длина пакета данных передалась не полностью
						byte[] lengthBytes = BitConverter.GetBytes(currentSendPacket.length);
						currentSendPacket.handledLength += socket.Send(lengthBytes, 4 + currentSendPacket.handledLength, -currentSendPacket.handledLength, 0);
						if(!currentSendPacket.IsLengthKnown)
							break;
					} else { //Если длина пакета уже передалась
						currentSendPacket.handledLength += socket.Send(currentSendPacket.data, currentSendPacket.handledLength, currentSendPacket.length - currentSendPacket.handledLength, 0);
						if(currentSendPacket.handledLength == currentSendPacket.length) {
							currentSendPacket = null;
						}  else
							break;
					}
				}
			} catch(SocketException e) {
				Console.WriteLine(e.Message);
				return false;
			}
			return true;
		}
	}
}
