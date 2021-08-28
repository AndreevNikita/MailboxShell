using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
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

	public partial class Mailbox {

		public static bool PRINT_ERRORS = false;

		public Socket Socket { get; private set; }
		readonly int maxPacketSize;

		private volatile int sendQueueSize;
		private volatile int receivedQueueSize;

		Channel<Packet> sendChannel;
		Channel<Packet> receivedChannel;

		Packet currentSendPacket = null;
		Packet currentReceivePacket = null;
		int maxReceiveFragmentsPerTick;
		int sendPacketsPerTick;

		public bool IsConnected { get => Socket.Connected; }
		public bool IsSendQueueEmpty { get => sendQueueSize == 0; }

		public int SendQueueSize { get => sendQueueSize; }
		public int ReceivedCount { get => receivedQueueSize; }

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
			this.maxReceiveFragmentsPerTick = maxReceiveFragmentsPerTick;
			this.sendPacketsPerTick = sendPacketsPerTick;
			this.maxPacketSize = maxPacketSize;
			this.sendChannel = Channel.CreateUnbounded<Packet>(new UnboundedChannelOptions() { SingleReader = true });
			this.receivedChannel = Channel.CreateUnbounded<Packet>(new UnboundedChannelOptions() { SingleWriter = true, SingleReader = true });
			SetSyncMode();
		}

		public void Send(Packet packet) {
			Interlocked.Increment(ref sendQueueSize);
			if(!sendChannel.Writer.TryWrite(packet))
				sendChannel.Writer.WriteAsync(packet).AsTask().Wait();
		}

		public Packet Next() {
			Packet packet;
			if(receivedChannel.Reader.TryRead(out packet)) {
				Interlocked.Decrement(ref receivedQueueSize);
				return packet;
			}
			return null;
		}


		public IEnumerable<Packet> GetAllReceived() { 
			for(int count = Interlocked.Exchange(ref receivedQueueSize, 0); count > 0; count--) { 
				if(receivedChannel.Reader.TryRead(out Packet packet)) {
					yield return packet;
				} else { 
					Interlocked.Add(ref receivedQueueSize, count);
					break;
				}
			}
		}

		
		[Obsolete("This Method is Deprecated (Works same as GetAllReceived)")]
		public IEnumerable<Packet> ForeachReceived() { 
			return GetAllReceived();
		}


		private byte[] receiveLengthBuffer = new byte[sizeof(int)];

		private bool CheckReceivedPacketLength(int length) => (maxPacketSize == 0 || currentReceivePacket.length <= maxPacketSize) && (currentReceivePacket.length > 0);

		public virtual bool Tick() {

			int packetsCounter;
			int fragmentsCounter;

			if(!Socket.Connected) {
				return false;
			}
			try {

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

							if(currentReceivePacket.length == 0) {
							} else if(!CheckReceivedPacketLength(currentReceivePacket.length)) { 
								Socket.Close();
								return false;
							} else {
								currentReceivePacket.CreateBuffer();
							}
						} else
							break;
					} 

					if(currentReceivePacket.IsLengthKnown) {
						if(Socket.Available != 0) { 
							try {
								currentReceivePacket.handledLength += Socket.Receive(currentReceivePacket.data, currentReceivePacket.handledLength, currentReceivePacket.length - currentReceivePacket.handledLength, 0);
							} catch(Exception e) { 
								Console.WriteLine(e);
								Console.WriteLine(e.StackTrace);
							}

							if(currentReceivePacket.handledLength == currentReceivePacket.length) { 
								if(!receivedChannel.Writer.TryWrite(currentReceivePacket))
									break;
								Interlocked.Increment(ref receivedQueueSize);
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
						if(!sendChannel.Reader.TryRead(out currentSendPacket)) {
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
							Interlocked.Decrement(ref sendQueueSize);
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

		//To use a socket in asynchronous mode (StartListenAsync)

		private void SetAsyncMode() { 
			Socket.Blocking = true;
		}

		//To use a socket in asynchronous mode (StartListenAsync)

		private void SetSyncMode() { 
			Socket.Blocking = false;
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

	public class MailboxUsingException : Exception { 

		public readonly Mailbox Mailbox;
		public MailboxUsingException(Mailbox mailbox, string message) : base(message) {
			Mailbox = mailbox;
		}
	}
}
