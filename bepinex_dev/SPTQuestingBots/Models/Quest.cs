﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using EFT;
using EFT.Game.Spawning;
using EFT.Interactive;
using Newtonsoft.Json;
using SPTQuestingBots.Controllers;

namespace SPTQuestingBots.Models
{
    public class Quest
    {
        [JsonProperty("repeatable")]
        public bool IsRepeatable { get; set; } = false;

        [JsonProperty("minLevel")]
        public int MinLevel { get; set; } = 0;

        [JsonProperty("maxLevel")]
        public int MaxLevel { get; set; } = 99;

        [JsonProperty("chanceForSelecting")]
        public float ChanceForSelecting { get; set; } = 50;

        [JsonProperty("priority")]
        public int Priority { get; set; } = 99;

        [JsonProperty("maxRaidET")]
        public float MaxRaidET { get; set; } = float.MaxValue;

        [JsonProperty("maxTimeOnQuest")]
        public float MaxTimeOnQuest { get; set; } = ConfigController.Config.BotQuestingRequirements.MaxTimePerQuest;

        [JsonProperty("canRunBetweenObjectives")]
        public bool CanRunBetweenObjectives { get; set; } = true;

        [JsonIgnore]
        public RawQuestClass Template { get; private set; } = null;

        [JsonProperty("name")]
        private string name = "Unnamed Quest";

        [JsonProperty("objectives")]
        private QuestObjective[] objectives = new QuestObjective[0];

        [JsonIgnore]
        private List<BotOwner> blacklistedBots = new List<BotOwner>();

        // A dictionary of the time when each bot in the dictionary completes its first objective in the quest. The key is removed when the bot
        // starts a different quest. 
        // TO DO: This name kinda sucks. 
        [JsonIgnore]
        private Dictionary<BotOwner, DateTime> activeBots = new Dictionary<BotOwner, DateTime>();

        // A dictionary to store all of the objectives each bot completes. The key is removed when the bot starts a different quest. 
        // TO DO: This name kinda sucks. 
        [JsonIgnore]
        private Dictionary<BotOwner, List<QuestObjective>> completedObjectives = new Dictionary<BotOwner, List<QuestObjective>>();

        public string Name => Template?.Name ?? name;
        public string TemplateId => Template?.TemplateId ?? "";
        
        // Return all objectives in the quest
        public ReadOnlyCollection<QuestObjective> AllObjectives => new ReadOnlyCollection<QuestObjective>(objectives);
        public int NumberOfObjectives => AllObjectives.Count;

        // Return all objectives in the quest that have valid positions for their first step
        public IEnumerable<QuestObjective> ValidObjectives => AllObjectives.Where(o => o.GetFirstStepPosition() != null);
        public int NumberOfValidObjectives => ValidObjectives.Count();

        // Return the bots that are currently doing this quest and have reached at least one objective
        // TO DO: This name kinda sucks. 
        public ReadOnlyCollection<BotOwner> ActiveBots => new ReadOnlyCollection<BotOwner>(activeBots.Keys.ToArray());

        public Quest()
        {

        }

        public Quest(int priority) : this()
        {
            Priority = priority;
        }

        public Quest(int priority, string _name): this(priority)
        {
            name = _name;
        }

        public Quest(int priority, RawQuestClass template) : this(priority)
        {
            Template = template;
        }

        public void Clear()
        {
            blacklistedBots.Clear();
            objectives = new QuestObjective[0];
        }

        // This is called when a bot completes an objective in the quest
        // TO DO: This name kinda sucks. 
        public void StartQuestForBot(BotOwner bot)
        {
            if (!activeBots.ContainsKey(bot))
            {
                activeBots.Add(bot, DateTime.Now);
            }
        }

        // This is called when a bot selects another quest
        // TO DO: This name kinda sucks. 
        public void StopQuestForBot(BotOwner bot)
        {
            if (activeBots.ContainsKey(bot))
            {
                activeBots.Remove(bot);
            }

            if (completedObjectives.ContainsKey(bot))
            {
                completedObjectives.Remove(bot);
            }
        }

        // This is called if a bot is unable to start the quest for some reason
        public void BlacklistBot(BotOwner bot)
        {
            if (!blacklistedBots.Contains(bot))
            {
                blacklistedBots.Add(bot);
            }

            StopQuestForBot(bot);
        }

        public bool CanAssignBot(BotOwner bot)
        {
            bool canAssign = !blacklistedBots.Contains(bot)
                && ((bot.Profile.Info.Level >= MinLevel) || !ConfigController.Config.BotQuestingRequirements.ExcludeBotsByLevel)
                && ((bot.Profile.Info.Level <= MaxLevel) || !ConfigController.Config.BotQuestingRequirements.ExcludeBotsByLevel)
                && LocationController.GetElapsedRaidTime() < MaxRaidET;

            return canAssign;
        }

        public void AddObjective(QuestObjective objective)
        {
            objectives = objectives.Append(objective).ToArray();
        }

        public bool TryRemoveObjective(QuestObjective objective)
        {
            if (objectives.Length == 0)
            {
                return true;
            }

            int startingLength = objectives.Length;
            objectives = objectives.Where(o => !o.Equals(objective)).ToArray();

            return startingLength == objectives.Length - 1;
        }

        public void CompleteObjective(BotOwner bot, QuestObjective objective)
        {
            if (!completedObjectives.ContainsKey(bot))
            {
                completedObjectives.Add(bot, new List<QuestObjective>() { objective } );
            }
            else
            {
                completedObjectives[bot].Add(objective);
            }
        }

        public bool HasBotCompletedAnyObjectives(BotOwner bot)
        {
            return completedObjectives.ContainsKey(bot);
        }

        public QuestObjective GetRandomObjective()
        {
            IEnumerable<QuestObjective> possibleObjectives = ValidObjectives
                .Where(o => o.CanAssignMoreBots);
            
            if (!possibleObjectives.Any())
            {
                return null;
            }

            return possibleObjectives.Random();
        }

        public QuestObjective GetRandomNewObjective(BotOwner bot)
        {
            // Don't allow bots to spend too much time doing a single quest
            if (activeBots.ContainsKey(bot))
            {
                TimeSpan timeSinceStarted = DateTime.Now - activeBots[bot];
                if (timeSinceStarted.TotalSeconds > MaxTimeOnQuest)
                {
                    LoggingController.LogWarning("Bot " + bot.Profile.Nickname + " has spent " + timeSinceStarted.TotalSeconds + " on quest " + Name + " and will choose another one.");
                    return null;
                }
            }

            IEnumerable<QuestObjective> possibleObjectives = RemainingObjectivesForBot(bot);
            if (!possibleObjectives.Any())
            {
                return null;
            }

            return possibleObjectives.Random();
        }

        public int GetRemainingObjectiveCount(BotOwner bot)
        {
            return RemainingObjectivesForBot(bot).Count();
        }

        public QuestObjective GetObjectiveForZoneID(string zoneId)
        {
            Func<QuestZoneObjective, bool> matchTest = o => o?.ZoneID == zoneId;
            return GetObjective(matchTest);
        }

        public QuestObjective GetObjectiveForLootItem(LootItem item)
        {
            Func<QuestItemObjective, bool> matchTest = o => o.Item?.TemplateId == item.TemplateId;
            return GetObjective(matchTest);
        }

        public QuestObjective GetObjectiveForLootItem(string templateID)
        {
            Func<QuestItemObjective, bool> matchTest = o => o.Item?.TemplateId == templateID;
            return GetObjective(matchTest);
        }

        public QuestObjective GetObjectiveForSpawnPoint(SpawnPointParams spawnPoint)
        {
            Func<QuestSpawnPointObjective, bool> matchTest = o => o.SpawnPoint?.Id == spawnPoint.Id;
            return GetObjective(matchTest);
        }

        private QuestObjective GetObjective<T>(Func<T, bool> matchTestFunc) where T : QuestObjective
        {
            IEnumerable<T> matchingObjectives = objectives
                .OfType<T>()
                .Where(o => matchTestFunc(o) == true);

            if (matchingObjectives.Count() == 0)
            {
                return null;
            }

            if (matchingObjectives.Count() > 1)
            {
                LoggingController.LogWarning("Found multiple quest objectives: " + string.Join(", ", matchingObjectives.Select(o => o.ToString())) + " for quest " + Name + ". Returning the first one.");
            }

            return matchingObjectives.First();
        }

        private IEnumerable<QuestObjective> RemainingObjectivesForBot (BotOwner bot)
        {
            return ValidObjectives
                .Where(o => o.CanAssignBot(bot))
                .Where(o => o.CanAssignMoreBots)
                .Where(o => !completedObjectives.ContainsKey(bot) || !completedObjectives[bot].Contains(o));
        }
    }
}
