using MailboxShell;
using NetBinSerializer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MailboxShell
{
	public class SendPacket : Packet {
		public readonly SerializeStream stream = new SerializeStream();

		public SendPacket() {}

		public SendPacket write(params object[] data) {
			writeArray(data);
			return this;
		}

		public SendPacket writeArray(object[] data) {
			foreach(object dataObj in data)
				stream.write(dataObj);
			return this;
		}

		public SendPacket flush() {
			if(length != 0) //For only one flush
				return this;

			this.data = stream.getBytes();
			handledLength = -4; //For send packet length
			length = this.data.Length;
			return this;
		}
		
	}
}
