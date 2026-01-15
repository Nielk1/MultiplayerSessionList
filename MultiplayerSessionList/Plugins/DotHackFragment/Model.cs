using Okolni.Source.Query.Responses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MultiplayerSessionList.Plugins.DotHackFragment
{
    /*
	public class LobbyServerData
	{
		[JsonPropertyName("playerList")]
		public Player[] PlayerList { get; set; }

		[JsonPropertyName("areaServerList")]
        public AreaServer[] AreaServerList { get; set; }
    }

    public class Player
    {
        [JsonPropertyName("saveSlot")]
        public byte saveSlot { get; set; }

        [JsonPropertyName("saveId")]
        public string saveId{ get; set; }

        [JsonPropertyName("charId")]
        public string charId { get; set; }

        [JsonPropertyName("charName")]
        public string charName { get; set; }

        [JsonPropertyName("charClass")]
        public string charClass { get; set; }

        [JsonPropertyName("charLevel")]
        public byte charLevel { get; set; }

        [JsonPropertyName("greeting")]
        public string greeting { get; set; }

        [JsonPropertyName("charModel")]
        public string charModel { get; set; }

        [JsonPropertyName("charHp")]
        public Int64 charHp { get; set; }

        [JsonPropertyName("charSp")]
        public Int64 charSp { get; set; }

        [JsonPropertyName("charGp")]
        public Int64 charGp { get; set; }

        [JsonPropertyName("onlineGodCounter")]
        public Int64 onlineGodCounter { get; set; }

        [JsonPropertyName("offlineGodCounter")]
        public Int64 offlineGodCounter { get; set; }

        [JsonPropertyName("charModelFile")]
        public string charModelFile { get; set; }
    }
    */
    public class LobbyPlayer
    {
        [JsonPropertyName("characterId")]
        public Int32 CharacterId { get; set; }

        [JsonPropertyName("characterName")]
        public string CharacterName { get; set; } = null!;

        [JsonPropertyName("joinedAt")]
        public DateTime JoinedAt { get; set; }
    }
    public enum ChatLobbyType : UInt16
    {
        Any = 0,
        Default = 0x7403,
        Chatroom = 0x7409,
        Guild = 0x7418,
    }
    public class ChatLobbyTypeJsonConverter : JsonConverter<ChatLobbyType>
    {
        public override ChatLobbyType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.GetString() switch
            {
                "Any" => ChatLobbyType.Any,
                "Default" => ChatLobbyType.Default,
                "Chatroom" => ChatLobbyType.Chatroom,
                "Guild" => ChatLobbyType.Guild,
                _ => throw new JsonException()
            };
        }

        public override void Write(Utf8JsonWriter writer, ChatLobbyType value, JsonSerializerOptions options)
        {
            var str = value switch
            {
                ChatLobbyType.Any => "Any",
                ChatLobbyType.Default => "Default",
                ChatLobbyType.Chatroom => "Chatroom",
                ChatLobbyType.Guild => "Guild",
                _ => throw new JsonException()
            };
            writer.WriteStringValue(str);
        }
    }
    public class Lobby
    {
        [JsonPropertyName("id")]
        public Int32 Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("playerCount")]
        public Int64 PlayerCount { get; set; } // 64bit assumed due to schema not listing size of integer

        [JsonPropertyName("type")]
        [JsonConverter(typeof(ChatLobbyTypeJsonConverter))]
        public ChatLobbyType Type { get; set; }

        [JsonPropertyName("players")]
        public LobbyPlayer[] Players { get; set; } = null!;
    }
    public enum AreaServerStatus : byte
    {
        Unknown = 0,
        Published = 3,
    }
    public enum AreaServerState : byte
    {
        Normal = 0,
        Password = 1,
        Playing = 2,
    }
    public class AreaServer
    {
        [JsonPropertyName("categoryId")]
        public Int32 CategoryId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("level")]
        public UInt16 Level { get; set; } // based on structure from server side code

        [JsonPropertyName("status")]
        public AreaServerStatus Status { get; set; }

        [JsonPropertyName("state")]
        public AreaServerState State { get; set; }

        [JsonPropertyName("currentPlayerCount")]
        public UInt16 CurrentPlayerCount { get; set; } // based on structure from server side code

        [JsonPropertyName("onlineSince")]
        public DateTime OnlineSince { get; set; }
    }
}
