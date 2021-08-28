using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MailboxShell
{
	public partial class Mailbox
	{
		private ManualResetEvent AsyncListenLocker = new ManualResetEvent(true);
		private Task listenTask = null;
		private CancellationTokenSource receiveCancellationTokenSource;
		private CancellationTokenSource sendCancellationTokenSource;

		private static async Task ReadBytesAsync(NetworkStream stream, byte[] buffer, int size, CancellationToken cancellationToken) { 
			int readCount = 0;
			while(readCount != size) {
				readCount += await stream.ReadAsync(buffer, readCount, size - readCount, cancellationToken);
			}
		}

		private static readonly TimeSpan ONE_SECOND = TimeSpan.FromSeconds(1);
		

		private async Task StartReceiveAsync(int receivePacketsPerSecond, CancellationToken cancellationToken) { 
			using(NetworkStream receiveStream = new NetworkStream(Socket)) {
				try { 
					while(IsConnected && !cancellationToken.IsCancellationRequested) { 
						DateTime currentStartOneSecondTime = DateTime.Now;
						for(int packetsCounter = 0; (receivePacketsPerSecond == 0) || (packetsCounter != receivePacketsPerSecond); packetsCounter++) {
						
							currentReceivePacket = new Packet();
							await ReadBytesAsync(receiveStream, receiveLengthBuffer, sizeof(int), cancellationToken);
							currentReceivePacket.length = BitConverter.ToInt32(receiveLengthBuffer, 0);
							if(currentReceivePacket.length == 0)
								continue;
							if(!CheckReceivedPacketLength(currentReceivePacket.length)) { 
								Socket.Close();
								return;
							}
							currentReceivePacket.CreateBuffer();
							await ReadBytesAsync(receiveStream, currentReceivePacket.data, currentReceivePacket.length, cancellationToken);
							currentReceivePacket.handledLength = currentReceivePacket.length;
							await receivedChannel.Writer.WriteAsync(currentReceivePacket);
							Interlocked.Increment(ref receivedQueueSize);
						}
						TimeSpan dtime = DateTime.Now - currentStartOneSecondTime;
						if(dtime < ONE_SECOND)
							await Task.Delay(ONE_SECOND - dtime);
					}
				} catch(Exception e) { 
					if(PRINT_ERRORS) {
						Console.WriteLine(e.Message);
						Console.WriteLine(e.StackTrace);
					}
				}
			}
		}

		private volatile bool InterruptSendingWhenAll = false;
		private async Task StartSendAsync(CancellationToken cancellationToken) {
			using(NetworkStream sendStream = new NetworkStream(Socket)) {
				try { 
					while(IsConnected && !cancellationToken.IsCancellationRequested) {
						Packet packet = await sendChannel.Reader.ReadAsync(cancellationToken);
						await sendStream.WriteAsync(BitConverter.GetBytes(packet.length), 0, sizeof(int), cancellationToken);
						if(packet.length != 0)
							await sendStream.WriteAsync(packet.data, 0, packet.length, cancellationToken);
						Interlocked.Decrement(ref sendQueueSize);
						if(InterruptSendingWhenAll && sendQueueSize == 0)
							break;
					}
				} catch(Exception e) { 
					if(PRINT_ERRORS) {
						Console.WriteLine(e.Message);
						Console.WriteLine(e.StackTrace);
					}
				}
			}
		}


		private async Task _StartListenAsync(int receivePacketsPerSecond, CancellationToken cancellationToken) {
			SetAsyncMode();
			receiveCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			InterruptSendingWhenAll = false;
			sendCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			await Task.WhenAll(
				StartReceiveAsync(receivePacketsPerSecond, receiveCancellationTokenSource.Token),
				StartSendAsync(sendCancellationTokenSource.Token)
			);
			SetSyncMode();
		}

		public Task StartListenAsync(CancellationToken cancellationToken) => StartListenAsync(0, cancellationToken);

		public Task StartListenAsync(int receivePacketsPerSecond = 0, CancellationToken cancellationToken = default) { 
			if(AsyncListenLocker.WaitOne(0)) {
				listenTask = _StartListenAsync(receivePacketsPerSecond, cancellationToken);
				listenTask.ContinueWith((Task task) => { AsyncListenLocker.Set(); });
			} else { 
				throw new MailboxUsingException(this, "The mailbox is already being asynchonously processed");
			}
			return listenTask;
		}

		public void StopListen(bool interruptSendingWhenAll = false) { 
			receiveCancellationTokenSource.Cancel();
			if(interruptSendingWhenAll) { 
				if(IsSendQueueEmpty) {
					sendCancellationTokenSource.Cancel();
				} else {
					InterruptSendingWhenAll = true;
				}
			} else {
				sendCancellationTokenSource.Cancel();
			
			}
		}

		public async Task StopListenAsync(bool interruptSendingWhenAll = false) { 
			StopListen(interruptSendingWhenAll);
			await listenTask;
		}

		public void StopListenWait(bool interruptSendingWhenAll = false) { 
			StopListenAsync(interruptSendingWhenAll).Wait();
		}


		public async Task<Packet> GetNextAsync(CancellationToken cancellationToken = default) { 
			return await receivedChannel.Reader.ReadAsync(cancellationToken);
		}

		public async Task<IEnumerable<Packet>> GetAllReceivedAsync(CancellationToken cancellationToken = default) { 
			await receivedChannel.Reader.WaitToReadAsync(cancellationToken);
			return GetAllReceived();
		}

	}
}
