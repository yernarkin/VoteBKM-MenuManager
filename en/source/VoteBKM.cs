using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Timers;
using MenuManager;
using CounterStrikeSharp.API.Core.Capabilities;


namespace VoteBanPlugin;

public class VoteBanPlugin : BasePlugin
{
    private Dictionary<string, int> _voteCounts = new Dictionary<string, int>();
    private bool _isVoteActionActive = false;
    private VoteBanConfig? _config;
    private Dictionary<string, HashSet<int>> _playerVotes = new Dictionary<string, HashSet<int>>(); // Players that voted for each candidate
    private Dictionary<int, string> _votedPlayers = new Dictionary<int, string>(); // Players that already voted
    private BannedPlayersConfig _bannedPlayersConfig;
    private string _bannedPlayersConfigFilePath;
    
    
    
    

    private const string PluginAuthor = "DoctorishHD";
    public override string ModuleName => "VoteBKM";
    public override string ModuleVersion => "1.0";
    public VoteBanConfig Config { get; set; }
	
	private IMenuApi? _api;
    private readonly PluginCapability<IMenuApi?> _pluginCapability = new("menu:nfcore"); 

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _api = _pluginCapability.Get();
        if (_api == null) Console.WriteLine("MenuManager Core not found...");
		
		_bannedPlayersConfigFilePath = Path.Combine(ModuleDirectory, "BannedPlayersConfig.json");
       

        RegisterEventHandler<EventPlayerSpawn>((@event, info) =>
        {
            HandlePlayerSpawnEvent(@event);
            return HookResult.Continue;
        });
        
        RegisterEventHandler<EventPlayerConnectFull>((@event, info) =>
        {
            UnbanExpiredPlayers(); // Проверяем и удаляем истекшие баны
			
			// NEW: IMMEDIATE KICK ON CONNECT
			if (@event.Userid is CCSPlayerController player && player.IsValid && !player.IsBot)
			{
				var steamId = player.SteamID.ToString();
				if (IsPlayerBanned(steamId))
				{
					Server.NextFrame(() =>  // Safe: kick next frame
						Server.ExecuteCommand($"kickid {player.UserId}"));
				}
			}
            return HookResult.Continue;
        });

        RegisterEventHandler<EventPlayerInfo>((@event, info) => 
        {
            HandlePlayerInfoEvent(@event);
            return HookResult.Continue;
        });
        
        LoadConfig();
        LoadBannedPlayersConfig();
        AddCommand("voteban", "Initiate voteban process", (player, command) => CommandVote(player, command, ExecuteBan));
        AddCommand("votemute", "Initiate votemute process", (player, command) => CommandVote(player, command, ExecuteMute));
        AddCommand("votekick", "Initiate votekick process", (player, command) => CommandVote(player, command, ExecuteKick));
        AddCommand("votereset", "Reset the voting process", CommandVoteReset);

        
        
    }

    private void HandlePlayerInfoEvent(EventPlayerInfo @event)
    {
        // Here you can get info about a player and save it for future use
        string playerName = @event.Name;
        ulong steamId = @event.Steamid;
        int userId = @event.Userid.UserId.Value;

        // Kick logic (if needed)
        // Example: if(shouldKick(playerName)) ExecuteKick(playerName);
    }

    private void HandlePlayerConnectFullEvent(EventPlayerConnectFull @event)
    {
        try
        {
            UnbanExpiredPlayers(); // Check and delete expired bans

            var userId = (int)(@event?.Userid?.Handle ?? -1);
            if (userId != -1)
            {
                var player = Utilities.GetPlayerFromUserid(userId);
                if (player != null && player.IsValid)
                {
                    string steamId = player.SteamID.ToString();
                    if (_bannedPlayersConfig.BannedPlayers.TryGetValue(steamId, out var bannedPlayerInfo))
                    {
						// Now variable bannedPlayerInfo is available and contains info about ban
						// You can use it for future logic
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VoteBanPlugin] Error in HandlePlayerConnectFullEvent: {ex.Message}");
        }
    }



    private void CheckAndReloadConfig()
    {
        string configFilePath = Path.Combine(ModuleDirectory, "voteban_config.json");
        if (!File.Exists(configFilePath) || Config == null)
        {
            LoadConfig();
        }
		// Here you can add additional checks and updates for a config
    }

    private void HandlePlayerSpawnEvent(EventPlayerSpawn @event)
    {
        try
        {
            if (@event.Userid != null && @event.Userid.IsValid)
            {
                string steamId = @event.Userid.SteamID.ToString();
                if (IsPlayerBanned(steamId))
                {
                    if (_bannedPlayersConfig.BannedPlayers.TryGetValue(steamId, out var bannedPlayerInfo))
                    {
                        var banEndTime = DateTimeOffset.FromUnixTimeSeconds(bannedPlayerInfo.BanEndTime).UtcDateTime;
                        banEndTime = ConvertToMoscowTime(banEndTime);
                        var currentTime = ConvertToMoscowTime(DateTime.UtcNow);

                        if (currentTime < banEndTime)
                        {
                            Console.WriteLine($"[VoteBanPlugin] Banned player {@event.Userid.PlayerName} (SteamID: {steamId}) is being kicked.");
                            Server.ExecuteCommand($"kickid {@event.Userid.UserId.Value}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VoteBanPlugin] Error in HandlePlayerSpawnEvent: {ex.Message}");
        }
    }






    public void OnConfigParsed(VoteBanConfig config)
        {
			// Execute check and tuning of config here (if needed)
            if (config.BanDuration > 600)
            {
                config.BanDuration = 600;
            }

            if (config.MinimumPlayersToStartVote < 2)
            {
                config.MinimumPlayersToStartVote = 2;
            }

			// Setting loaded and checked config
            Config = config;
        }


        
   private void LoadConfig()
    {
        string configFilePath = Path.Combine(ModuleDirectory, "voteban_config.json");
        if (!File.Exists(configFilePath))
        {
            Config = new VoteBanConfig();
            string jsonConfig = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configFilePath, jsonConfig);
        }
        else
        {
            Config = JsonSerializer.Deserialize<VoteBanConfig>(File.ReadAllText(configFilePath)) ?? new VoteBanConfig();
        }
		// Here you can add additional parameters for config
    }



    [GameEventHandler]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        try
        {
            // Используйте приведение типа для конвертации nint в int
			// I'm bad in programming and english but here it says something about used type of conversion from nint to int
            int userId = (int)@event.Userid.Handle;
            var player = Utilities.GetPlayerFromUserid(userId);
            if (player != null)
            {
                string disconnectedPlayerName = player.PlayerName;
                if (disconnectedPlayerName != null && _playerVotes.ContainsKey(disconnectedPlayerName))
                {
                    _playerVotes.Remove(disconnectedPlayerName);

                    foreach (var voteEntry in _votedPlayers.ToList().Where(entry => entry.Value == disconnectedPlayerName))
                    {
                        _votedPlayers.Remove(voteEntry.Key);
                    }

                    Server.PrintToChatAll($"[VoteBKM] Voting for {disconnectedPlayerName} has been cancelled as player left the server.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling player disconnect: {ex.Message}");
        }
        return HookResult.Continue;
    }



    private void ResetVotingProcess()
    {
        _isVoteActionActive = false;
        _voteCounts.Clear();
        _playerVotes.Clear();
        _votedPlayers.Clear();
    }

    private void CommandVoteReset(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || !IsAdminWithFlag(player, "@css/ban")) // Swap "admin_flag" to the one needed for use of this command
        {
            player?.PrintToChat("[VoteBKM] You don't have permission to use this command.");
            return;
        }

        ResetVotingProcess();
        player.PrintToChat("[VoteBKM] Voting has been reset.");
    }

    

    private bool IsAdminWithFlag(CCSPlayerController? player, string? flag)
    {
        if (player == null || flag == null) return false;
        return AdminManager.PlayerHasPermissions(player, flag);
    }

   private void CommandVote(CCSPlayerController? player, CommandInfo commandInfo, Action<string> executeAction)
    {
        if (player == null)
        {
            Console.WriteLine("[VoteBanPlugin] Error: Player value is NULL.");
            return;
        }

        if (Config == null)
        {
            Console.WriteLine("[VoteBanPlugin] Error: Config value is NULL.");
            return;
        }

		// Get list of active players excluding ones who left the server (UserId 65535)
        var activePlayers = Utilities.GetPlayers().Where(p => p.UserId.Value != 65535).ToList();
        if (activePlayers.Count < Config.MinimumPlayersToStartVote)
        {
            player.PrintToChat($"[VoteBKM] Not enough active players on server to start a vote.");
            return;
        }

		// Calculating required number of votes based on the number of active players
        int requiredVotes = (int)Math.Ceiling(activePlayers.Count * Config.RequiredMajority);

		// Checking if there's enough active players to start a vote
        if (activePlayers.Count >= requiredVotes)
        {
            ShowVoteMenu(player, executeAction);
        }
        else
        {
            player.PrintToChat($"[VoteBKM] For action to happen {requiredVotes} required votes are needed.");
        }
    }





    private void ShowVoteMenu(CCSPlayerController player, Action<string> executeAction)
    {
        var voteMenu = _api.GetMenu("Choose a Player");
        foreach (var p in Utilities.GetPlayers())
        {
            if (IsAdminWithFlag(p, "@css/ban") || p.UserId.Value == 65535)
                continue;

            string playerName = p.PlayerName;
            voteMenu.AddMenuOption(playerName, (voter, option) => HandleVote(voter, playerName, executeAction));
        }
        voteMenu.Open(player);
    }

    private void HandleVote(CCSPlayerController voter, string targetPlayerName, Action<string> executeAction)
    {
        if (voter == null || string.IsNullOrEmpty(targetPlayerName) || executeAction == null)
        {
            Console.WriteLine("[VoteBanPlugin] Error: voter, targetPlayerName or ExecuteAction value(s) is NULL.");
            return;
        }

		// Ignoring player votes that have UserId 65535 (disconnected/invalid)
        if (voter.UserId.Value == 65535)
        {
            Console.WriteLine("[VoteBanPlugin] Ignoring player vote with UserId 65535 (disconnected/invalid).");
            return;
        }

        int voterUserId = voter.UserId.Value;

		// Checking if player had already voted
        if (_votedPlayers.TryGetValue(voterUserId, out var previousVote))
        {
            if (previousVote != targetPlayerName && _playerVotes.ContainsKey(previousVote))
            {
                _playerVotes[previousVote].Remove(voterUserId);
            }
        }

		// Adding/updating vote entry
        if (!_playerVotes.ContainsKey(targetPlayerName))
        {
            _playerVotes[targetPlayerName] = new HashSet<int>();
        }

        _playerVotes[targetPlayerName].Add(voterUserId);
        _votedPlayers[voterUserId] = targetPlayerName;

		// Counting active votes excluding UserId(s) 65535
        var activeVotes = _playerVotes.Where(vote => vote.Value.All(id => Utilities.GetPlayerFromUserid(id)?.UserId.Value != 65535)).ToDictionary(entry => entry.Key, entry => entry.Value);

        int currentVotes = activeVotes.ContainsKey(targetPlayerName) ? activeVotes[targetPlayerName].Count : 0;
        int activePlayersCount = Utilities.GetPlayers().Count(p => p.UserId.Value != 65535);
        int requiredVotes = (int)Math.Ceiling(activePlayersCount * Config.RequiredMajority);

		// Printing current votes' count into the chat
        Server.PrintToChatAll($"[VoteBKM] Current votes for {targetPlayerName}: {currentVotes}/{requiredVotes}");

		// Checking if required number of votes is reached
        if (currentVotes >= requiredVotes)
        {
            executeAction(targetPlayerName);
            ResetVotingProcess();
        }
    }







    private void StartBanCheckTimer(CCSPlayerController player)
    {
        var timer = new Timer(2.0f, () => BanCheckTimerElapsed(player), TimerFlags.STOP_ON_MAPCHANGE);
    }

     private void BanCheckTimerElapsed(CCSPlayerController player)
    {
        string steamId = player.SteamID.ToString();
        if (IsPlayerBanned(steamId))
        {
            Console.WriteLine($"[VoteBanPlugin] {player.PlayerName} (SteamID: {steamId}) was Banned and Kicked from the server");
            Server.ExecuteCommand($"kickid {player.UserId}");
        }
    }



    private DateTime ConvertToMoscowTime(DateTime time)
    {
        var moscowZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        return TimeZoneInfo.ConvertTimeFromUtc(time, moscowZone);
    }



   private void CheckAndKickBannedPlayer(CCSPlayerController player, string steamId)
    {
        LoadBannedPlayersConfig();
        if (_bannedPlayersConfig != null && IsPlayerBanned(steamId))
        {
            var bannedPlayerInfo = _bannedPlayersConfig.BannedPlayers[steamId];
            var banEndTime = DateTimeOffset.Parse(bannedPlayerInfo.BanEndTime.ToString("o"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            var currentTime = ConvertToMoscowTime(DateTime.UtcNow);

            if (currentTime < banEndTime)
            {
                Console.WriteLine($"[VoteBanPlugin] Banned player {player.PlayerName} (SteamID: {steamId}) is being kicked.");
                Server.ExecuteCommand($"kickid {player.UserId.Value}");
            }
        }
    }



    private CCSPlayerController? GetPlayerFromSteamID(string steamId)
    {
        return Utilities.GetPlayers().FirstOrDefault(p => p.SteamID.ToString() == steamId);
    }

    private string? GetSteamIDFromPlayerName(string playerName)
    {
        var player = Utilities.GetPlayers().FirstOrDefault(p => p.PlayerName.Equals(playerName, StringComparison.OrdinalIgnoreCase));
        return player?.SteamID.ToString();
    }

    private CCSPlayerController? GetPlayerFromName(string playerName)
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player.PlayerName.Equals(playerName, StringComparison.OrdinalIgnoreCase))
            {
                return player;
            }
        }
        return null;
    }


    private void ExecuteBan(string identifier)
    {
		// Checking if config was initialized
        if (Config == null)
        {
            Console.WriteLine("[VoteBanPlugin] Error: Config is null.");
            return;
        }

		// Attempting to find a player
        var player = GetPlayerFromName(identifier) ?? GetPlayerFromSteamID(identifier);

		// Checking if player was found
        if (player == null)
        {
            Console.WriteLine($"[VoteBanPlugin] Error: Player with identifier '{identifier}' not found.");
            return;
        }

		// Checking if UserId or SteamID was initialized
        if (!player.UserId.HasValue || string.IsNullOrEmpty(player.SteamID.ToString()))
        {
            Console.WriteLine($"[VoteBanPlugin] Error: Player '{player.PlayerName}' has invalid UserId or SteamID.");
            return;
        }

		// Get player's SteamID
        string steamId = player.SteamID.ToString();

		// Checkin if player is already banned
        if (IsPlayerBanned(steamId))
        {
            Console.WriteLine($"[VoteBanPlugin] Player {player.PlayerName} (SteamID: {steamId}) is already banned.");
            return;
        }

		// Executing player ban
        BanPlayer(steamId, Config.BanDuration);

        Console.WriteLine($"[VoteBanPlugin] Player {player.PlayerName} (SteamID: {steamId}) has been banned.");
    }



    private void ExecuteMute(string identifier)
    {
        var playerToMute = GetPlayerFromName(identifier) ?? GetPlayerFromSteamID(identifier);
        
        if (playerToMute == null)
        {
            Console.WriteLine($"[VoteBanPlugin] Error: Player with identifier '{identifier}' not found.");
            return;
        }

        if (!playerToMute.UserId.HasValue || string.IsNullOrEmpty(playerToMute.SteamID.ToString()))
        {
            Console.WriteLine($"[VoteBanPlugin] Error: Player '{playerToMute.PlayerName}' has invalid UserId or SteamID.");
            return;
        }

        if (IsPlayerMuted(playerToMute))
        {
            Console.WriteLine($"[VoteBanPlugin] Player {playerToMute.PlayerName} (SteamID: {playerToMute.SteamID}) is already muted.");
            return;
        }

        MutePlayer(playerToMute);

        Console.WriteLine($"[VoteBanPlugin] Player {playerToMute.PlayerName} (SteamID: {playerToMute.SteamID}) has been muted.");
    }

    private void MutePlayer(CCSPlayerController player)
    {
        player.VoiceFlags = VoiceFlags.Muted;
		// Here you can add additional logic to Mute if you need to save info on mute
    }

    private bool IsPlayerMuted(CCSPlayerController player)
    {
        return player.VoiceFlags.HasFlag(VoiceFlags.Muted);
    }

    private void ExecuteKick(string identifier)
    {
		// Attempting to find a player by Name or SteamID
        var player = GetPlayerFromName(identifier) ?? GetPlayerFromSteamID(identifier);

        if (player != null && player.UserId.HasValue)
        {
			// Execute Kick command
            Server.ExecuteCommand($"kickid {player.UserId.Value}");
            Server.PrintToChatAll($"[VoteKick] Player {player.PlayerName} was kicked from the server.");
        }
        else
        {
			// If player wasn't found
            Console.WriteLine($"[VoteBanPlugin] Player with name or SteamID {identifier} has not been found.");
        }
    }


    private void LoadBannedPlayersConfig()
    {
		// Checking existence of banned players' config file (existence is probably the wrong word here)
        if (File.Exists(_bannedPlayersConfigFilePath))
        {
            try
            {
				// Reading JSON from file
                string json = File.ReadAllText(_bannedPlayersConfigFilePath);

                // Десериализация JSON в объект BannedPlayersConfig
				// Deserializing JSON into object "BannedPlayersConfig"?
                _bannedPlayersConfig = JsonSerializer.Deserialize<BannedPlayersConfig>(json);

				// If deserializing returns NULL then create new config file
                if (_bannedPlayersConfig == null)
                {
                    _bannedPlayersConfig = new BannedPlayersConfig();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading banned players config: {ex.Message}");
				// In case of an error in reading the file -> create new config file
                _bannedPlayersConfig = new BannedPlayersConfig();
            }
        }
        else
        {
			// If config file doesn't exist -> create new config file
            _bannedPlayersConfig = new BannedPlayersConfig();
        }
    }


    private void SaveBannedPlayersConfig()
    {
        string json = JsonSerializer.Serialize(_bannedPlayersConfig, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_bannedPlayersConfigFilePath, json);
    }

    private void BanPlayer(string steamId, int durationInSeconds)
    {
        var banEndTime = DateTimeOffset.UtcNow.AddSeconds(durationInSeconds).ToUnixTimeSeconds();
        string? nickname = GetPlayerFromSteamID(steamId)?.PlayerName;

        var bannedPlayerInfo = new BannedPlayerInfo
        {
            BanEndTime = banEndTime,
            Nickname = nickname
        };

        _bannedPlayersConfig.BannedPlayers[steamId] = bannedPlayerInfo;
        SaveBannedPlayersConfig();

        var player = GetPlayerFromSteamID(steamId);
        if (player != null && player.UserId.HasValue)
        {
            Server.ExecuteCommand($"kickid {player.UserId.Value}");
        }
    }

    private DateTime ConvertFromUnixTimestamp(long timestamp)
    {
        var dateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        return dateTimeOffset.LocalDateTime;
    }

    private bool IsPlayerBanned(string steamId)
    {
        if (_bannedPlayersConfig.BannedPlayers.TryGetValue(steamId, out var bannedPlayerInfo))
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() < bannedPlayerInfo.BanEndTime;
        }
        return false;
    }

     private void UnbanExpiredPlayers()
    {
        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expiredBans = _bannedPlayersConfig.BannedPlayers
            .Where(kvp => kvp.Value.BanEndTime < currentTime)
            .ToList();

        foreach (var kvp in expiredBans)
        {
            var steamId = kvp.Key;
            var bannedPlayerInfo = kvp.Value; // Теперь bannedPlayerInfo объявлена в этом контексте // Now bannedPlayerInfo announced?/used?/initiated? in this context?

			// Here you can use bannedPlayerInfo for future logic
            // Например, для вывода информации о истекшем бане или его удаления
			// For instance, to give info on an expired ban or its removal

            _bannedPlayersConfig.BannedPlayers.Remove(steamId);
        }

		// Checking existence of expired bans and saving the config
        if (expiredBans.Any())
        {
            SaveBannedPlayersConfig();
        }
    }


    public class BannedPlayersConfig
    {
        public Dictionary<string, BannedPlayerInfo> BannedPlayers { get; set; } = new Dictionary<string, BannedPlayerInfo>();
    }

    public class BannedPlayerInfo
    {
        public long BanEndTime { get; set; } // Changed to long
        public string Nickname { get; set; }
        public string SteamID { get; set; }
    }

    public class VoteBanConfig : BasePluginConfig
    {
        [JsonPropertyName("BanDuration")]
        public int BanDuration { get; set; } = 3600;

        [JsonPropertyName("RequiredMajority")]
        public double RequiredMajority { get; set; } = 0.5;

        [JsonPropertyName("MinimumPlayersToStartVote")]
        public int MinimumPlayersToStartVote { get; set; } = 4;
    }

}
 