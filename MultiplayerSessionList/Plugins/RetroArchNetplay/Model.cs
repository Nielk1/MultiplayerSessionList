using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json.Serialization;
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
		[JsonPropertyName("id")]
		public Int32 RoomID { get; set; }
		[JsonPropertyName("username")]
		public string Username { get; set; }
		[JsonPropertyName("country")]
		public string Country { get; set; }
		[JsonPropertyName("game_name")]
		public string GameName { get; set; }
		[JsonPropertyName("game_crc")]
		public string GameCRC { get; set; }
		[JsonPropertyName("core_name")]
		public string CoreName { get; set; }
		[JsonPropertyName("core_version")]
		public string CoreVersion { get; set; }
		[JsonPropertyName("subsystem_name")]
		public string SubsystemName { get; set; }
		[JsonPropertyName("retroarch_version")]
		public string RetroArchVersion { get; set; }
		[JsonPropertyName("frontend")]
		public string Frontend { get; set; }
		[JsonPropertyName("ip")]
		public string IP { get; set; }
		[JsonPropertyName("port")]
		public UInt16 Port { get; set; }
		[JsonPropertyName("mitm_ip")]
		public string MitmAddress { get; set; }
		[JsonPropertyName("mitm_port")]
		public UInt16 MitmPort { get; set; }
		[JsonPropertyName("host_method")]
		public HostMethod HostMethod { get; set; }
		[JsonPropertyName("has_password")]
		public bool HasPassword { get; set; }
		[JsonPropertyName("has_spectate_password")]
		public bool HasSpectatePassword { get; set; }
		[JsonPropertyName("created")]
		public DateTime CreatedAt { get; set; }
		[JsonPropertyName("updated")]
		public DateTime UpdatedAt { get; set; }
	}
}
