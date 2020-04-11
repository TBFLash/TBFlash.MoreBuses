using System;
using System.Collections.Generic;
using SimAirport.Modding.Base;
using SimAirport.Modding.Data;
using SimAirport.Modding.Settings;
using SimAirport.Logging;
using UnityEngine;
using Agent;

namespace TBFlash.MoreBuses
{
    public class Mod : BaseMod
    {
        public override string Name => "More Buses";

        public override string InternalName => "TBFlash.MoreBuses";

        public override string Description => "This Mod doubles the number of buses for each dropoff/pickup zone. Additionally, enabling the 30 Second Spawn Cooldown will decrease the delay between each bus (and other vehicles). Note that running the game at higher than 1x speed may result in some buses not arriving or backups in your delivery/pickup zones.";

        public override string Author => "TBFlash";

        public override SettingManager SettingManager { get; set; }

        private int busInterval = 60;

        private bool enabled = false;

        private int morebuses_next_bus;

        private int paxPerHourBuses;

        private int paxPerHourTotal;

        /// <summary>
        /// Local access to settings and load status; loaded in OnLoad()
        /// </summary>
        private LabelSetting paxPerHourLabel;

        private bool paxPerHourLabelLoaded = false;

        private LabelSetting descriptionLabel;

        private bool descriptionLabelLoaded = false;

        private CheckboxSetting cooldownCheckbox;

        private bool cooldownLoaded = false;

        /// <summary>
        /// Enable debug messages into Player.log
        /// </summary>
        private readonly bool moreBusesDebug = false;

        private enum Labels
        {
            PaxPerHour,
            Description,
            Cooldown
        }

        /// <summary>
        /// Resets the spawn cooldown and resets the paxPerHour label when the mod is disabled.
        /// </summary>
        public override void OnDisabled()
        {
            MoreBusesLogging("OnDisabled");
            ChangeSpawnCooldown(false);
            enabled = false;
            CalculateNumPax();
        }

        /// <summary>
        /// Sets parameters when the mod is loaded/enabled.
        /// </summary>
        /// <param name="state"></param>
        public override void OnLoad(SimAirport.Modding.Data.GameState state)
        {
            MoreBusesLogging("OnLoad");

            if (!(paxPerHourLabelLoaded && descriptionLabelLoaded && cooldownLoaded))
            {
                paxPerHourLabelLoaded = SettingManager.TryGetSetting<LabelSetting>("PaxPerHour", out paxPerHourLabel);
                descriptionLabelLoaded = SettingManager.TryGetSetting<LabelSetting>("Description", out descriptionLabel);
                cooldownLoaded = SettingManager.TryGetSetting<CheckboxSetting>("CooldownSetting", out cooldownCheckbox);
                MoreBusesLogging(string.Format("Loading labels: PaxPerHour - {0}, Description - {1}, Cooldown - {2}", paxPerHourLabelLoaded, descriptionLabelLoaded, cooldownLoaded));
            }

            if (Game.isLoaded)
            {
                if (!SetBusInterval())
                {
                    RecalculateBusSpawnTime();
                }

                if (SettingManager.TryGetBool("cooldownSetting", out bool cooldownSettingValue))
                {
                    ChangeSpawnCooldown(cooldownSettingValue);
                }

                enabled = true;
                CalculateNumPax();
                ResetLabel(Labels.Description);
                ResetLabel(Labels.Cooldown);
            }
        }

        public override void OnSettingsLoaded()
        {
            MoreBusesLogging("OnSettingsLoaded started");

            CheckboxSetting cooldownSetting = new CheckboxSetting
            {
                Name = "30 Second Spawn Cooldown",
                Value = false,
                SortOrder = 5,
                OnValueChanged = new Action<bool>((bool changeVar) =>
                {
                    MoreBusesLogging(string.Format("30 Second Spawn Cooldown changed to {0}", changeVar));
                    ChangeSpawnCooldown(changeVar);
                })
            };
            SettingManager.AddDefault("CooldownSetting", cooldownSetting);

            LabelSetting description = new LabelSetting
            {
                Name = "",
                SortOrder = 100
            };
            SettingManager.AddDefault("Description", description);

            LabelSetting paxPerHour = new LabelSetting
            {
                Name = "",
                SortOrder = 90
            };
            SettingManager.AddDefault("PaxPerHour", paxPerHour);
        }

        /// <summary>
        /// OnTick method that initiates bus spawning and setting of the bus interval.
        /// </summary>
        public override void OnTick()
        {
            if (!Game.isLoaded || GameTimer.Speed == 0f)
            {
                return;
            }

            int minute = GameTimer.Minute;
            if (minute >= morebuses_next_bus)
            {
                RecalculateBusSpawnTime();
                MoreBusesLogging(string.Format("Spawning Bus - Minute {0} NextBus {1}", minute, morebuses_next_bus));
                EnqueueBuses(false);
            }

            if (GameTimer.Second % 60 == 0)
            {
                SetBusInterval();
            }

            if (GameTimer.Second % 300 == 0)
            {
                CalculateNumPax();
            }
        }

        /// <summary>
        /// Calculates the number of pax for the PaxPerHour label
        /// </summary>
        private void CalculateNumPax()
        {
            int numPaxOnLightrail = 0;
            if (TechTreeLevel.TechLevelReached(TechTreeLevel.TechTreeLevels.Lightrail))
            {
                numPaxOnLightrail += (int)((float)LightRailTrain.MaxCapacity * (60f / (float)Game.current.spawner.lightrail_interval));
            }

            int multiplier = 1;
            if (enabled)
            {
                multiplier = 2;
            }

            int numPaxOnBuses = 0;
            int dropoffZoneCount = Game.current.Map().ZonesByType(Zone.ZoneType.Dropoffs).Count;
            if (TechTreeLevel.TechLevelReached(TechTreeLevel.TechTreeLevels.UpgradedBuses))
            {
                numPaxOnBuses += (int)((float)(150 * dropoffZoneCount * multiplier) * (60f / (float)Game.current.spawner.bus_interval));
            }
            else
            {
                numPaxOnBuses += (int)((float)(75 * dropoffZoneCount * multiplier) * (60f / (float)Game.current.spawner.bus_interval));
            }

            if (!(paxPerHourTotal == numPaxOnLightrail + numPaxOnBuses && paxPerHourBuses == numPaxOnBuses))
            {
                paxPerHourTotal = numPaxOnLightrail + numPaxOnBuses;
                paxPerHourBuses = numPaxOnBuses;
                ResetLabel(Labels.PaxPerHour);
            }
        }

        /// <summary>
        /// Changes the t_spawnCooldown parameter the Spawner class
        /// </summary>
        /// <param name="speedup"></param>
        private void ChangeSpawnCooldown(bool speedup)
        {
            if (Game.isLoaded)
            {
                if (speedup)
                {
                    Game.current.spawner.t_spawnCooldown = 30;
                    MoreBusesLogging("Spawn Cooldown changed to 30 seconds");
                }
                else
                {
                    Game.current.spawner.t_spawnCooldown = 60;
                    MoreBusesLogging("Spawn Cooldown changed to 60 seconds");
                }
            }
        }

        /// <summary>
        /// Rewrite of Spawner.EnqueueBuses to remove the test to see if there are already buses on the queue. This means that this method will always add buses to the spawn queue.
        /// </summary>
        /// <param name="freeBus"></param>
        private void EnqueueBuses(bool freeBus = false)
        {
            bool upgraded = TechTreeLevel.TechLevelReached(TechTreeLevel.TechTreeLevels.UpgradedBuses);
            int pickupZoneCount = 0;
            foreach(Zone localPUZone in Game.current.Map().ZonesByType(Zone.ZoneType.Pickups))
            {
                VehicleBus vehicleBus = Bus(upgraded, freeBus);
                vehicleBus.pickupZone = localPUZone as PickupsZone;
                vehicleBus.initialState = BaseAgent.State.InboundPickup;
                pickupZoneCount++;
            }

            int dropoffZoneCount = 0;
            foreach(Zone localDOZone in Game.current.Map().ZonesByType(Zone.ZoneType.Dropoffs))
            {
                VehicleBus vehicleBus2 = Bus(upgraded, freeBus);
                vehicleBus2.dropoffs = localDOZone;
                vehicleBus2.initialState = BaseAgent.State.InboundDropoff;
                dropoffZoneCount++;
            }

            if (!freeBus)
            {
                Game.current._money.ChangeBalance(-25.0 * (double)(dropoffZoneCount + pickupZoneCount), i18n.Get("UI.money.reason.BusService", ""), GamedayReportingData.MoneyCategory.Transportation, -1);
            }
            MoreBusesLogging(string.Format("{0} Buses were enqueued", dropoffZoneCount + pickupZoneCount));
        }

        /// <summary>
        /// Writes class messages to Game.Logger
        /// </summary>
        /// <param name="text"></param>
        private void MoreBusesLogging(string text)
        {
            if (moreBusesDebug)
            {
                Game.Logger.Write(Log.FromPool("MOD - MOREBUSES - " + text));
            }
        }

        /// <summary>
        /// Sets the time that the next buses will spawn from this mod.
        /// </summary>
        private void RecalculateBusSpawnTime()
        {
            float num = (float)GameTimer.Minute + 1f;
            morebuses_next_bus = Mathf.CeilToInt(num / (float)busInterval) * busInterval;
            MoreBusesLogging(string.Format("Recalculated Bus Spawn Time - Next time: {0}", morebuses_next_bus));
        }

        /// <summary>
        /// Changes labels in the settings menu
        /// </summary>
        private void ResetLabel(Labels labelToChange)
        {
            switch(labelToChange)
            {
                case Labels.PaxPerHour when paxPerHourLabelLoaded:
                    paxPerHourLabel.Name = string.Format(i18n.Get("TBFlash.MoreBuses.paxPerHourLabel.name", ""), paxPerHourBuses, paxPerHourTotal, Environment.NewLine);
                    MoreBusesLogging(string.Format("Resetting the paxPerHourLabel to Buses-{0}; Total-{1}", paxPerHourBuses, paxPerHourTotal));
                    return;
                case Labels.Description when descriptionLabelLoaded:
                    descriptionLabel.Name = i18n.Get("TBFlash.MoreBuses.description", "");
                    MoreBusesLogging("Resetting the description");
                    return;
                case Labels.Cooldown when cooldownLoaded:
                    cooldownCheckbox.Name = i18n.Get("TBFlash.MoreBuses.cooldownSetting.name", "");
                    MoreBusesLogging("Resetting the cooldown name");
                    return;
                default:
                    MoreBusesLogging(string.Format("Attempt to change label {0} failed", labelToChange));
                    return;
            }
        }

        /// <summary>
        /// Sets the local busInterval to the same value as Spawner.bus_interval; ensures that new buses are added to the spawn queue at the same time as Spawner adds them.
        /// </summary>
        /// <returns>bool</returns>
        private bool SetBusInterval()
        {
            if (busInterval != Game.current.spawner.bus_interval)
            {
                busInterval = Game.current.spawner.bus_interval;
                MoreBusesLogging(string.Format("Bus interval set to {0}", busInterval));
                RecalculateBusSpawnTime();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Copy of the Spawner.Bus. Needed here because it is private in the Spawner class.
        /// </summary>
        /// <param name="upgraded"></param>
        /// <param name="freeBus"></param>
        /// <returns>VehicleBus</returns>
        private VehicleBus Bus(bool upgraded, bool freeBus)
        {
            VehicleBus vehicleBus = ObjectPool.current.FetchInstance<VehicleBus>("Vehicles/Passenger Bus", -1);
            vehicleBus.Capacity = (upgraded ? 150 : 75);
            Game.current.spawner.RequestVehicle(vehicleBus, (double)(freeBus ? 100 : 10));
            return vehicleBus;
        }
    }
}