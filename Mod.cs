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

        public override void OnAirportLoaded(Dictionary<string, object> saveData)
        {
        }
        //Resets the spawn cooldown and resets the paxPerHour label when the mod is disabled.
        public override void OnDisabled()
        {
            MoreBusesLogging("OnDisabled");
            ChangeSpawnCooldown(false);
            this.enabled = false;
            CalculateNumPax();
        }
        //Sets parameters when the mod is loaded/enabled.
        public override void OnLoad(SimAirport.Modding.Data.GameState state)
        {
            MoreBusesLogging("OnLoad");
            if (Game.isLoaded)
            {
                bool flag = SetBusInterval();
                if (!flag)
                {
                    RecalculateBusSpawnTime();
                }
                bool cooldownSettingValue;
                flag = this.SettingManager.TryGetBool("cooldownSetting", out cooldownSettingValue);
                if(flag)
                {
                    ChangeSpawnCooldown(cooldownSettingValue);
                }
                this.enabled = true;
                CalculateNumPax();
            }
        }
        public override void OnSettingsLoaded()
        {
            MoreBusesLogging("OnSettingsLoaded started");

            //cooldownSetting allows users to decrease the time between spawns to 30 seconds, rather than every minute.
            CheckboxSetting cooldownSetting = new CheckboxSetting
            {
                Name = "30 Second Spawn Cooldown",
                Value = false,
                SortOrder = 5,
                OnValueChanged = new Action<bool>(delegate (bool changeVar)
                {
                    MoreBusesLogging(string.Format("30 Second Spawn Cooldown changed to {0}", changeVar));
                    ChangeSpawnCooldown(changeVar);
                })
            };
            this.SettingManager.AddDefault("cooldownSetting", cooldownSetting);

            //description provides a description of the mod to users within the ModSettings panel.
            LabelSetting description = new LabelSetting
            {
                Name = this.Description,
                SortOrder = 100
            };
            this.SettingManager.AddDefault("Description", description);

            //paxPerHour displays the new estimated number of pax per hour when the Mod is active.
            LabelSetting paxPerHour = new LabelSetting
            {
                Name = "",
                SortOrder = 90
            };
            this.SettingManager.AddDefault("PaxPerHour", paxPerHour);
        }

        //OnTick method that initiates bus spawning and setting of the bus interval.
        public override void OnTick()
        {
            if (!Game.isLoaded || GameTimer.Speed == 0f)
            {
                return;
            }

            int minute = GameTimer.Minute;
            if (minute >= this.morebuses_next_bus)
            {
                RecalculateBusSpawnTime();
                MoreBusesLogging(string.Format("Spawning Bus - Minute {0} NextBus {1}", minute, this.morebuses_next_bus));
                EnqueueBuses(false);
            }
            if (GameTimer.Second % 60 == 0)
            {
                SetBusInterval();
            }
            if (GameTimer.Minute % 5 == 0)
            {
                CalculateNumPax();
            }
        }
        //Calculates the number of pax for the PaxPerHour label
        private void CalculateNumPax()
        {
            int num1 = 0;
            if (TechTreeLevel.TechLevelReached(TechTreeLevel.TechTreeLevels.Lightrail))
            {
                num1 += (int)((float)LightRailTrain.MaxCapacity * (60f / (float)Game.current.spawner.lightrail_interval));
            }
            int num2 = 0;
            int count = Game.current.Map().ZonesByType(Zone.ZoneType.Dropoffs).Count;
            int multiplier=1;
            if(this.enabled)
            {
                multiplier = 2;
            }
            if (TechTreeLevel.TechLevelReached(TechTreeLevel.TechTreeLevels.UpgradedBuses))
            {
                num2 += (int)((float)(150 * count * multiplier) * (60f / (float)Game.current.spawner.bus_interval));
            }
            else
            {
                num2 += (int)((float)(75 * count * multiplier) * (60f / (float)Game.current.spawner.bus_interval));
            }
            num1 += num2;
            if (!(this.paxPerHourTotal == num1) || !(this.paxPerHourBuses == num2))
            {
                this.paxPerHourTotal = num1;
                this.paxPerHourBuses = num2;
                ResetPaxPerHourLabel();
            }
        }
        //Changes the t_spawnCooldown parameter the Spawner class
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

       //Rewrite of Spawner.EnqueueBuses to remove the test to see if there are already buses on the queue. This means that this method will always add buses to the spawn queue.
        private void EnqueueBuses(bool freeBus = false)
        {
            bool upgraded = TechTreeLevel.TechLevelReached(TechTreeLevel.TechTreeLevels.UpgradedBuses);
            List<Zone> list2 = Game.current.Map().ZonesByType(Zone.ZoneType.Pickups);
            int count = list2.Count;
            for (int i = 0; i < count; i++)
            {
                VehicleBus vehicleBus = this.Bus(upgraded, freeBus);
                vehicleBus.pickupZone = (list2[i] as PickupsZone);
                vehicleBus.initialState = BaseAgent.State.InboundPickup;
            }
            List<Zone> list = Game.current.Map().ZonesByType(Zone.ZoneType.Dropoffs);
            int num = Math.Max(list.Count, 1);
            for (int j = 0; j < num; j++)
            {
                VehicleBus vehicleBus2 = this.Bus(upgraded, freeBus);
                if (list.Count > j)
                {
                    vehicleBus2.dropoffs = list[j];
                }
                vehicleBus2.initialState = BaseAgent.State.InboundDropoff;
            }
            if (!freeBus)
            {
                Game.current._money.ChangeBalance(-25.0 * (double)(num + count), i18n.Get("UI.money.reason.BusService", ""), GamedayReportingData.MoneyCategory.Transportation, -1);
            }
            MoreBusesLogging(string.Format("{0} Buses were enqueued", num + count));
        }
        //Writes class messages to Game.Logger
        private void MoreBusesLogging(string text)
        {
            if (this.moreBusesDebug)
            {
                Game.Logger.Write(Log.FromPool("MOD - MOREBUSES - " + text));
            }
        }

        //Sets the time that the next buses will spawn from this mod.
        private void RecalculateBusSpawnTime()
        {
            float num = (float)GameTimer.Minute + 1f;
            this.morebuses_next_bus = Mathf.CeilToInt(num / (float)this.busInterval) * this.busInterval;
            MoreBusesLogging(string.Format("Recalculated Bus Spawn Time - Next time: {0}", this.morebuses_next_bus));
        }
        //Changes the PaxPerHourLabel
        private void ResetPaxPerHourLabel()
        {
            LabelSetting paxPerHourLabel;
            bool flag = this.SettingManager.TryGetSetting<LabelSetting>("PaxPerHour", out paxPerHourLabel);
            if (!flag)
            {
                MoreBusesLogging("Error getting paxPerHour Label");
            }
            else
            {
                paxPerHourLabel.Name = string.Format("Estimated Pax per Hour: Buses - {0}; Total - {1}\n    (Resets every 5 minutes)", this.paxPerHourBuses, this.paxPerHourTotal);
                MoreBusesLogging(string.Format("Resetting the paxPerHourLabel to Buses-{0}; Total-{1}", this.paxPerHourBuses, this.paxPerHourTotal));
            }
        }
        //sets the local busInterval to the same value as Spawner.bus_interval; ensures that new buses are added to the spawn queue at the same time as Spawner adds them.
        private bool SetBusInterval()
        {
            if (this.busInterval != Game.current.spawner.bus_interval)
            {
                this.busInterval = Game.current.spawner.bus_interval;
                MoreBusesLogging(string.Format("Bus interval set to {0}", this.busInterval));
                RecalculateBusSpawnTime();
                return true;
            }
            return false;
        }
        //Copy of the Spawner.Bus. Needed here because it is private in the Spawner class.
        private VehicleBus Bus(bool upgraded, bool freeBus)
        {
            VehicleBus vehicleBus = ObjectPool.current.FetchInstance<VehicleBus>("Vehicles/Passenger Bus", -1);
            vehicleBus.Capacity = (upgraded ? 150 : 75);
            Game.current.spawner.RequestVehicle(vehicleBus, (double)(freeBus ? 100 : 10));
            return vehicleBus;
        }
        public override SettingManager SettingManager { get; set; }
        private int busInterval = 60;
        private bool enabled = false;
        private int morebuses_next_bus;
        private int paxPerHourBuses;
        private int paxPerHourTotal;
        private readonly bool moreBusesDebug = false;

    }
}