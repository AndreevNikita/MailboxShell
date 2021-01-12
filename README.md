# MailboxShell
Library, that provides Mailbox class. The Mailbox class is a shell for a Socket object for a more simple network packets sending and receiving.
So it segments TCP traffic for packets and returns data in this view.

## How to use
1. Get the Socket object
2. Create Mailbox by `Mailbox mailbox = new Mailbox(socket)`

**Network packets sending and receiving completes by `mailbox.tick();` function in the NonBlocking mode. I.E. the best solution is to call this method with some interval (you can create a special thread to call this). `mailbox.tick();` returns false when an error occurs or remote host is diconnected**

## Send a packet
1. Create Packet object by `Packet packet = new Packet([yourdata : byte[]]);` (for example `Packet packet = new Packet(Encoding.UTF8.GetBytes("Hello!"))`)
2. To send packet call `mailbox.Send(packet)`
3. Call `mailbox.Tick()`

## Receive a packet
1. Call `mailbox.Tick()` in main or other thread with some time intervals
2. To receive packet on connected host use `mailbox.Next()` to get a packet if any packet is received (null else) or `mailbox.GetAllReceived()` to get all received packets Enumerator or if your update frequency is enought it's better to use `mailbox.swapGetReceived()` to get all received from last to current tick, but this function is unsafe with `mailbox.next()` (SimpleMultithreadQueue). I.E. you can use `mailbox.next()` amd `mailbox.getAllReceived()` or `mailbox.swapGetReceived()`
**!!!Don't forget to call `mailbox.tick()`!!!**
3. Received data bytes array is in packet.data field

## Mailbox's owner
* You can set owner object of mailbox by `void SetOwner(IMailboxOwner)` method and get it by `TYPE GetOwner<TYPE>()` method.  
* **Owner object** must implement **IMailboxOwner** interface and have **MailboxSafe** object  
For example:
```c#
public class ClientInfo : IMailboxOwner { 

	public MailboxSafe MailboxSafe { get; } = new MailboxSafe();
	
}
```

* Also call `mailbox.RemoveOwner()` to remove **owner object** link.
* Owner link methods are thread-safe, but don't use it too often. The purpose of these methods is to bind mailbox owner object in the start of handling
* Also you can use `GetMailbox`, `SetMailbox`, `RemoveMailbox` **IMailboxOwner's** extension methods. They call similar methods in owned mailbox



## Example
(You can find the Test project in this repository MailboxShell solution)
Lightweight example:

#### Client
```c#
Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
socket.Connect(IPAddress.Parse(SERVER_IP), SERVER_PORT);
Mailbox mailbox = new Mailbox(socket);

while(true) {
	string message = Console.ReadLine();
	mailbox.Send(new Packet(Encoding.UTF8.GetBytes(message)));
	while(true) {
		mailbox.Tick();
		Packet receivedPacket = mailbox.Next(); //Try to get the next packet
		if(receivedPacket != null) {
			Console.WriteLine($"Server response: \"{Encoding.UTF8.GetString(receivedPacket.data)}\"");
			break;			
		}
			
		Thread.sleep(1);
	}
}
```

#### Server
```c#
Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
serverSocket.Bind(new IPEndPoint(IPAddress.Parse(ip), port));
serverSocket.Listen(10);
Mailbox mailbox = new Mailbox(serverSocket.Accept());
while(true) {
	mailbox.Tick(); //Tick to receive
	foreach(Packet packet in mailbox.SwapGetReceived()) {
		string message = Encoding.UTF8.GetString(packet.data); //Get message from packet.data
		mailbox.Send(new Packet(Encoding.UTF8.GetBytes("Echo: " + message))); //Send response
	}
	mailbox.Tick(); //Tick to send (but both ticks can be in one in this case)
	Thread.sleep(1);
}
```