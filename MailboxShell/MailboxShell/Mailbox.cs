using SimpleMultithreadQueue;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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
				CreateBuffer();
		}

		public Packet(byte[] data) {
			this.data = data;
			this.length = data.Length;
			this.handledLength = -4;
		}

		internal void CreateBuffer() { 
			data = new byte[length];
		}

		public bool IsLengthKnown {
			get { 
				return handledLength >= 0;
			}
		}

		public Packet CloneDataRef() { 
			return new Packet(data);
		}

		internal void LockData() { }
	}

	public class DynamicPacket : Packet { 
		internal Mailbox owner;

		private bool dataFixed = false;

		public DynamicPacket(byte[] data) : base(data) {
			this.data = null;
		}

		public bool ReplaceData(byte[] newData) {
			if(dataFixed)
				return false;

			if(Monitor.TryEnter(data)) {
				if(dataFixed)
					return false;

				data = newData;
				return true;
			} else { 
				return false;
			}
		}

		internal void FixData() { 
			Monitor.Enter(data);
			dataFixed = true;
			Monitor.Exit(data);
		}
	}

	public class Mailbox {

		public static bool PRINT_ERRORS = false;

		public Socket Socket { get; private set; }
		readonly int maxPacketSize;
		MultithreadQueue<Packet> sendQueue = new MultithreadQueue<Packet>();
		MultithreadQueue<IEnumerator<Packet>> sendPartsQueue = new MultithreadQueue<IEnumerator<Packet>>();
		LinkedList<IEnumerator<Packet>> sendPartsList = new LinkedList<IEnumerator<Packet>>();

		MultithreadQueue<Packet> receivedQueue = new MultithreadQueue<Packet>();

		Packet currentSendPacket = null;
		object currentSendPacketMutex = new object();
		Packet currentReceivePacket = null;
		int maxReceiveFragmentsPerTick;
		int sendPacketsPerTick;

		public bool IsConnected { get => Socket.Connected; }
		public bool IsSendQueueEmpty { get => sendQueue.R_IsEmpty(); }


		private IMailboxOwner _MailboxOwner;
		private static object OwnerChangeMutex { get; } = new object();

		public IMailboxOwner MailboxOwner { 
			get => _MailboxOwner;
			set => SetOwner(value); 
		}

		public void RemoveOwner() { 
			lock(OwnerChangeMutex) {
				if(_MailboxOwner != null) {
					_MailboxOwner.MailboxSafe.Mailbox = null;
					_MailboxOwner = null;
				}
			}
		}

		public void SetOwner(IMailboxOwner newOwner) {
			if(newOwner == null) { 
				RemoveOwner();
				return;
			}

			lock(OwnerChangeMutex) {
				//Remove new owner's mailbox's owner
				if(newOwner.MailboxSafe.Mailbox != null) { 
					newOwner.MailboxSafe.Mailbox.MailboxOwner = null;
				}
				//Remove this mailbox link from last owner
				if(_MailboxOwner != null) { 
					_MailboxOwner.MailboxSafe.Mailbox = null;
				}

				newOwner.MailboxSafe.Mailbox = this;
				_MailboxOwner = newOwner;
			}
		}

		public TYPE GetOwner<TYPE>() where TYPE : IMailboxOwner {
			return (TYPE)_MailboxOwner;
		}

		public Mailbox(Socket socket, int maxReceiveFragmentsPerTick = 64, int sendPacketsPerTick = 0, int maxPacketSize = 0) {
			this.Socket = socket;
			socket.Blocking = false; //Перевод сокета в неблокируемый режим
			this.maxReceiveFragmentsPerTick = maxReceiveFragmentsPerTick;
			this.sendPacketsPerTick = sendPacketsPerTick;
			this.maxPacketSize = maxPacketSize;
		}

		public void Send(Packet packet) {
			sendQueue.Enqueue(packet);
		}

		public Packet Next() {
			Packet packet;
			if(receivedQueue.R_Dequeue(out packet))
				return packet;
			return null;
		}

		public IEnumerable<Packet> GetAllReceived() { 
			return receivedQueue.R_PopAllToNewQueue();
		}

		public IEnumerable<Packet> ForeachReceived() { 
			return receivedQueue.R_PopAll();
		}

		private byte[] receiveLengthBuffer = new byte[sizeof(int)];

		public void ClearReceivedQueue() { 
			receivedQueue.R_Clear();
		}

		public void ClearSendQueue() { 
			sendQueue.R_Clear();
		}

		public void ClearAll() { 
			ClearReceivedQueue();
			ClearSendQueue();
		}

		public virtual bool Tick() {
			int packetsCounter;
			int fragmentsCounter;

			if(!Socket.Connected)
				return false;
			try {
				foreach(IEnumerator<Packet> parts in sendPartsQueue)
					sendPartsList.AddLast(parts);

				fragmentsCounter = 0;
				while(true) {
					if(currentReceivePacket == null)
						currentReceivePacket = new Packet();

					if(!currentReceivePacket.IsLengthKnown) { 
						if(Socket.Available >= receiveLengthBuffer.Length) {
							currentReceivePacket.handledLength += Socket.Receive(receiveLengthBuffer, 0, receiveLengthBuffer.Length, 0);
							currentReceivePacket.length = BitConverter.ToInt32(receiveLengthBuffer, 0);
						
							if(!currentReceivePacket.IsLengthKnown)
								break;
							currentReceivePacket.CreateBuffer();
						} else
							break;
					} 

					if(currentReceivePacket.IsLengthKnown) {
						if(Socket.Available != 0) { 
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
								receivedQueue.Enqueue(currentReceivePacket);
								currentReceivePacket = null;
							} else
								break;
						} else
							break;
						fragmentsCounter++;
						if(maxReceiveFragmentsPerTick != 0 && fragmentsCounter == maxReceiveFragmentsPerTick)
							break;
					}
					
				}
			
				packetsCounter = 0;
				while(true) {
					
					if(currentSendPacket == null) {
						if(!sendQueue.R_DequeueReady(out currentSendPacket)) {
							break;
						}
						if(currentSendPacket == null) { 
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
							if(sendPacketsPerTick != 0 && packetsCounter == sendPacketsPerTick)
								break;
						}  else
							break;
					}
				}
			} catch(SocketException e) {
				if(PRINT_ERRORS) {
					Console.WriteLine(e.Message);
					Console.WriteLine(e.StackTrace);
				}
				return false;
			}
			return true;
		}

		public void Close() { 
			Socket.Close();
		}

	}


	public class MailboxSafe { 

		public Mailbox Mailbox { get; internal set; }

		public static explicit operator Mailbox(MailboxSafe safe) { 
			return safe.Mailbox;
		}

	}

	public interface IMailboxOwner { 

		MailboxSafe MailboxSafe { get; }

	}

	public static class MailboxOwnerExtendion {
		public static Mailbox GetMailbox(this IMailboxOwner owner) {
			return owner.MailboxSafe.Mailbox;
		}

		public static void SetMailbox(this IMailboxOwner owner, Mailbox mailbox) {
			mailbox.SetOwner(owner);
		}

		public static void RemoveMailbox(this IMailboxOwner owner) {
			owner.GetMailbox()?.RemoveOwner();
		}
	}
}
