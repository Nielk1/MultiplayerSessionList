using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Plugins.RetroArchNetplay
{
	public class SessionWrapper
	{
		public Session fields { get; set; }
	}
	public enum HostMethod
	{
		HostMethodUnknown = 0,
		HostMethodManual = 1,
		HostMethodUPNP = 2,
		HostMethodMITM = 3,
	}

	public class Session
	{
		[JsonProperty("id")]
		public Int32 RoomID { get; set; }
		[JsonProperty("username")]
		public string Username { get; set; }
		[JsonProperty("country")]
		public string Country { get; set; }
		[JsonProperty("game_name")]
		public string GameName { get; set; }
		[JsonProperty("game_crc")]
		public string GameCRC { get; set; }
		[JsonProperty("core_name")]
		public string CoreName { get; set; }
		[JsonProperty("core_version")]
		public string CoreVersion { get; set; }
		[JsonProperty("subsystem_name")]
		public string SubsystemName { get; set; }
		[JsonProperty("retroarch_version")]
		public string RetroArchVersion { get; set; }
		[JsonProperty("frontend")]
		public string Frontend { get; set; }
		[JsonProperty("ip")]
		public string IP { get; set; }
		[JsonProperty("port")]
		public UInt16 Port { get; set; }
		[JsonProperty("mitm_ip")]
		public string MitmAddress { get; set; }
		[JsonProperty("mitm_port")]
		public UInt16 MitmPort { get; set; }
		[JsonProperty("host_method")]
		public HostMethod HostMethod { get; set; }
		[JsonProperty("has_password")]
		public bool HasPassword { get; set; }
		[JsonProperty("has_spectate_password")]
		public bool HasSpectatePassword { get; set; }
		[JsonProperty("created")]
		public DateTime CreatedAt { get; set; }
		[JsonProperty("updated")]
		public DateTime UpdatedAt { get; set; }
	}
}
