﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using MEC;
using PlayerRoles;
using PluginAPI.Events;
using Respawning;

namespace SillySCP
{
    public class Plugin : Plugin<Config>
    {
        public static Plugin Instance;
        public List<PlayerStat> PlayerStats;
        private EventHandler _handler;
        public static DiscordSocketClient Client;
        public bool RoundStarted { get; set; }
        public PluginAPI.Core.Player Scp106;
        public List<Volunteers> Volunteers;
        public bool ReadyVolunteers;

        public override void OnEnabled()
        {
            RoundStarted = false;
            Instance = this;
            _handler = new EventHandler();
            EventManager.RegisterEvents(this, _handler);
            Exiled.Events.Handlers.Player.Left += OnPlayerLeave;
            Exiled.Events.Handlers.Server.WaitingForPlayers += OnWaitingForPlayers;
            Task.Run(StartClient);
            base.OnEnabled();
        }

        public override void OnDisabled()
        {
            RoundStarted = false;
            EventManager.UnregisterEvents(this, _handler);
            Exiled.Events.Handlers.Player.Left -= OnPlayerLeave;
            Exiled.Events.Handlers.Server.WaitingForPlayers -= OnWaitingForPlayers;
            _handler = null;
            Task.Run(StopClient);
            base.OnDisabled();
        }

        private void OnWaitingForPlayers()
        {
            Scp106 = null;
            RoundStarted = false;
            SetStatus();
        }

        private void OnPlayerLeave(LeftEventArgs ev)
        {
            if(Volunteers == null)
                return;
            var volunteeredScp = Volunteers.FirstOrDefault((v) => v.Players.Contains(ev.Player));
            if (volunteeredScp != null) volunteeredScp.Players.Remove(ev.Player);
        }

        private static Task DiscLog(LogMessage msg)
        {
            PluginAPI.Core.Log.Info(msg.ToString());
            return Task.CompletedTask;
        }

        private static async Task StartClient()
        {
            var config = new DiscordSocketConfig()
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages,
                LogLevel = LogSeverity.Error,
            };
            Client = new DiscordSocketClient(config);
            Client.Log += DiscLog;
            Client.Ready += Instance.Ready;
            await Client.LoginAsync(TokenType.Bot, Instance.Config.Token);
            await Client.StartAsync();
        }

        private static async Task StopClient()
        {
            await Client.StopAsync();
            await Client.LogoutAsync();
        }

        public void SetStatus()
        {
            string players = "";
            var textChannel = GetChannel(1279544677334253610);
            var playerList = Player.List;
            foreach (var player in playerList)
            {
                players += "- " + player.Nickname + "\n";
            }
            var embedBuilder = new EmbedBuilder()
                .WithTitle("Silly SCP Member List")
                .WithColor(Color.Blue)
                .WithDescription(
                    RoundStarted
                        ? players == ""
                            ? "No players online"
                            : players
                        : "Waiting for players."
                );
            SetMessage(textChannel, 1280910252325339311, embedBuilder.Build());
            SetCustomStatus();
        }

        private SocketTextChannel GetChannel(ulong id)
        {
            var channel = Client.GetChannel(id);
            if (channel.GetChannelType() != ChannelType.Text)
                return null;
            var textChannel = (SocketTextChannel)channel;
            return textChannel;
        }

        private async Task Ready()
        {
            SetStatus();
        }

        private bool _messageCooldown;

        private void SetMessage(SocketTextChannel channel, ulong messageId, Embed embed)
        {
            var message = channel.GetMessageAsync(messageId).Result;

            try
            {
                channel.ModifyMessageAsync(
                    message.Id,
                    msg =>
                    {
                        msg.Embed = embed;
                        msg.Content = "";
                    }
                );
                _messageCooldown = false;
            }
            catch
            {
                if(!_messageCooldown)
                {
                    _messageCooldown = true;
                    Timing.CallDelayed(10, () =>
                    {
                        if (!_messageCooldown) return;
                        SetMessage(channel, messageId, embed);
                    });
                }
            }
        }
        
        private bool _statusCooldown;

        private void SetCustomStatus()
        {
            var status = RoundStarted
                ? $"{Server.PlayerCount}/{Server.MaxPlayerCount} players"
                : "Waiting for players.";
            try
            {
                Client.SetCustomStatusAsync(status);
                _statusCooldown = false;
            }
            catch
            {
                if (!_statusCooldown)
                {
                    _statusCooldown = true;
                    Timing.CallDelayed(10, () =>
                    {
                        if (!_statusCooldown) return;
                        SetCustomStatus();
                    });
                }
            }
        }

        public PlayerStat FindOrCreatePlayerStat(PluginAPI.Core.Player player)
        {
            var playerStat = FindPlayerStat(player);
            if (playerStat == null)
            {
                playerStat = new PlayerStat
                {
                    Player = player,
                    Kills = player.DoNotTrack ? (int?)null : 0,
                    Spectating = null,
                };
                PlayerStats.Add(playerStat);
            }

            return playerStat;
        }
        
        public PlayerStat FindPlayerStat(PluginAPI.Core.Player player)
        {
            if(PlayerStats == null)
                PlayerStats = new List<PlayerStat>();
            return PlayerStats.Find((p) => p.Player == player);
        }

        public void UpdateKills(PluginAPI.Core.Player player, bool positive = true)
        {
            if (player.DoNotTrack)
                return;
            var playerStat = FindOrCreatePlayerStat(player);
            if (positive)
                playerStat.Kills++;
            else if (playerStat.Kills > 0)
                playerStat.Kills--;
        }

        public IEnumerator<float> RespawnTimer(PluginAPI.Core.Player player)
        {
            while (true)
            {
                yield return Timing.WaitForSeconds(1f);
                player = PluginAPI.Core.Player.GetPlayers().FirstOrDefault((p) => p.UserId == player.UserId);
                if (player == null || player.Role != RoleTypeId.Spectator)
                    break;
                var respawnTeam = Respawn.NextKnownTeam;
                var teamText = respawnTeam != SpawnableTeamType.None ? "<color=" + (respawnTeam == SpawnableTeamType.ChaosInsurgency ? "green>Chaos Insurgency" : "blue>Nine-Tailed Fox") + "</color>" : null;
                var timeUntilWave = Respawn.TimeUntilSpawnWave;
                timeUntilWave = teamText != null ? timeUntilWave : timeUntilWave.Add(TimeSpan.FromSeconds(Respawn.NtfTickets >= Respawn.ChaosTickets ? 17 : 13));
                var currentTime = $"{timeUntilWave.Minutes:D1}<size=22>M</size> {timeUntilWave.Seconds:D2}<size=22>S</size>";
                var playerStat = FindPlayerStat(player);
                var spectatingPlayerStat = playerStat != null && playerStat.Spectating != null ? FindPlayerStat(playerStat?.Spectating) : null;
                var spectatingKills =
                    spectatingPlayerStat != null
                        ? spectatingPlayerStat.Player.DoNotTrack == false ? spectatingPlayerStat.Kills.ToString() : "Unknown"
                        : "0";
                var text =
                    "<voffset=-4em><size=26>Respawning "
                    + (teamText != null ? "as " + teamText + " " : "")
                    + "in:\n</voffset>"
                    + currentTime
                    + "</size>"
                    + (playerStat?.Spectating != null ? "\n\nKill count: " + spectatingKills : "");
                player.ReceiveHint(text, 1.2f);
            }

            yield return 0;
        }

        public IEnumerator<float> DisableVolunteers()
        {
            ReadyVolunteers = true;
            yield return Timing.WaitForSeconds(180);
            ReadyVolunteers = false;
        }

        public string GetSCPNumber(RoleTypeId id)
        {
            switch (id)
            {
                case RoleTypeId.Scp049:
                    return "049";
                case RoleTypeId.Scp079:
                    return "079";
                case RoleTypeId.Scp096:
                    return "096";
                case RoleTypeId.Scp106:
                    return "106";
                case RoleTypeId.Scp173:
                    return "173";
                case RoleTypeId.Scp939:
                    return "939";
                default:
                    return null;
            }
        }

        public IEnumerator<float> ChooseVolunteers(Volunteers volunteer)
        {
            yield return Timing.WaitForSeconds(15);
            volunteer = Volunteers.FirstOrDefault((v) => v.Replacement == volunteer.Replacement);
            if (volunteer == null)
                yield break;
            var random = new Random();
            var replacementPlayer = volunteer.Players[random.Next(volunteer.Players.Count)];
            replacementPlayer.Role.Set(volunteer.Replacement);
            Map.Broadcast(10, "SCP-" + GetSCPNumber(volunteer.Replacement) + " has been replaced!",
                Broadcast.BroadcastFlags.Normal, true);
            yield return 0;
        }
    }
}
