using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using System;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Water Works", "nivex", "1.1.3")]
    [Description("Control the monopoly on your water supplies.")]
    class WaterWorks : RustPlugin
    {
        private readonly List<string> _itemShortnames = new()
        {
            "bucket.water",
            "waterjug",
            "pistol.water",
            "gun.water",
            "smallwaterbottle",
            "botabag"
        };

        private readonly Dictionary<string, ItemConfig> _itemConfigs = new();

        private void Init()
        {
            Unsubscribe(nameof(OnEntitySpawned));
        }

        private void OnServerInitialized()
        {
            foreach (var shortname in _itemShortnames)
            {
                ItemDefinition def = ItemManager.FindItemDefinition(shortname);
                if (def != null && def.TryGetComponent(out ItemModContainer container))
                {
                    var itemConfig = new ItemConfig
                    {
                        Shortname = shortname,
                        ItemId = def.itemid,
                        DefaultMaxStackSize = container.maxStackSize,
                        ConfigMaxStackSize = GetConfigValue(shortname)
                    };

                    _itemConfigs[shortname] = itemConfig;

                    if (itemConfig.ConfigMaxStackSize > 0)
                    {
                        container.maxStackSize = itemConfig.ConfigMaxStackSize;
                    }
                }
            }

            SetLiquidContainerStackSize(true);
            ConfigureWaterDefinitions(true);
            Subscribe(nameof(OnEntitySpawned));

            if (WaterTypes.WaterItemDef.stackable < int.MaxValue)
            {
                WaterTypes.WaterItemDef.stackable = int.MaxValue;
            }
        }

        private void Unload()
        {
            if (config.Reset && !Interface.Oxide.IsShuttingDown)
            {
                SetLiquidContainerStackSize(false);
                ConfigureWaterDefinitions(false);
            }
        }

        private void OnEntitySpawned(LiquidContainer lc)
        {
            SetLiquidContainerStackSize(lc, true);
        }
        private void SetLiquidContainerStackSize(bool state)
        {
            foreach (BaseNetworkable networkable in BaseNetworkable.serverEntities)
            {
                if (networkable is LiquidContainer lc)
                {
                    SetLiquidContainerStackSize(lc, state);
                }
            }
        }

        private void SetLiquidContainerStackSize(LiquidContainer lc, bool state)
        {
            if (lc == null || lc.IsDestroyed)
            {
                return;
            }

            if (lc is WaterWell well)
            {
                if (config.WaterWell.Enabled) UpdateWaterWell(well, state);
                return;
            }

            switch (lc.ShortPrefabName)
            {
                case "waterbarrel":
                    UpdateContainer(lc, state, config.WaterBarrel);
                    break;

                case "water_catcher_small":
                    UpdateWaterCatcher(lc as WaterCatcher, state, config.SmallWaterCatcher.StackSize, config.SmallWaterCatcher.MaxItemToCreate, config.SmallWaterCatcher.Interval);
                    break;

                case "water_catcher_large":
                    UpdateWaterCatcher(lc as WaterCatcher, state, config.LargeWaterCatcher.StackSize, config.LargeWaterCatcher.MaxItemToCreate, config.LargeWaterCatcher.Interval);
                    break;

                case "poweredwaterpurifier.deployed":
                case "waterpurifier.deployed":
                    UpdateWaterPurifier(lc as WaterPurifier, state);
                    break;

                case "water.pump.deployed":
                    UpdateWaterPump(lc as WaterPump, state);
                    break;

                case "paddlingpool.deployed":
                    UpdateContainer(lc, state, config.GroundPool);
                    break;

                case "abovegroundpool.deployed":
                    UpdateContainer(lc, state, config.BigGroundPool);
                    break;
            }
        }

        private void SetSlotAmounts(LiquidContainer lc, int num)
        {
            if (num > 0)
            {
                Item slot = lc.inventory.GetSlot(0);

                if (slot != null && slot.amount > num)
                {
                    slot.amount = num;
                }
            }
        }

        private static void SetMaxStackSize(LiquidContainer lc, int num)
        {
            lc.maxStackSize = num;
            lc.inventory.maxStackSize = num;
            lc.inventory.MarkDirty();
            // lc.MarkDirtyForceUpdateOutputs();
            lc.SendNetworkUpdateImmediate();
        }

        private void ConfigureWaterDefinitions(bool state)
        {
            foreach (var itemConfig in _itemConfigs.Values)
            {
                ItemDefinition def = ItemManager.FindItemDefinition(itemConfig.Shortname);
                if (def != null && def.TryGetComponent(out ItemModContainer container))
                {
                    if (state && itemConfig.ConfigMaxStackSize > 0)
                    {
                        container.maxStackSize = itemConfig.ConfigMaxStackSize;
                    }
                    else if (!state && itemConfig.DefaultMaxStackSize > 0)
                    {
                        container.maxStackSize = itemConfig.DefaultMaxStackSize;
                    }
                }
            }
        }

        private void UpdateContainer(LiquidContainer lc, bool state, int configuredCapacity)
        {
            int capacity = state && configuredCapacity > 0 ? configuredCapacity : lc.LookupPrefab<LiquidContainer>()?.maxStackSize ?? 0;
            if (capacity > 0)
            {
                SetMaxStackSize(lc, capacity);
                SetSlotAmounts(lc, capacity);
            }
        }

        private void UpdateWaterCatcher(WaterCatcher wc, bool state, int configuredCapacity, float configuredAmount, float configuredInterval)
        {
            if (wc == null) return;

            WaterCatcher prefab = wc.LookupPrefab<WaterCatcher>();
            int capacity = state && configuredCapacity > 0 ? configuredCapacity : prefab?.maxStackSize ?? 0;
            if (capacity > 0)
            {
                wc.maxStackSize = wc.inventory.maxStackSize = capacity;
                SetSlotAmounts(wc, capacity);
            }

            if (state && configuredAmount > 0f) wc.maxItemToCreate = configuredAmount;
            else if (prefab != null) wc.maxItemToCreate = prefab.maxItemToCreate;

            float interval = state && configuredInterval > 0f ? configuredInterval : prefab?.overrideCollectInterval ?? wc.overrideCollectInterval;
            if (wc.overrideCollectInterval != interval)
            {
                wc.overrideCollectInterval = interval;
                float nextInterval = interval > 0f ? interval : WaterCatcher.collectInterval;
                wc.nextCollect = nextInterval + UnityEngine.Random.Range(0f, nextInterval * 0.1f);
            }
        }

        private void UpdateWaterPurifier(WaterPurifier purifier, bool state)
        {
            if (purifier == null) return;

            WaterPurifier prefab = purifier.LookupPrefab<WaterPurifier>();
            bool powered = purifier is PoweredWaterPurifier;
            int configuredInput = powered ? config.PoweredWaterPurifier : config.WaterPurifier;
            int inputCapacity = state && configuredInput > 0 ? configuredInput : prefab?.maxStackSize ?? 0;
            if (inputCapacity > 0)
            {
                SetMaxStackSize(purifier, inputCapacity);
                if (powered) SetSlotAmounts(purifier, inputCapacity);
            }

            LiquidContainer output = purifier.purifiedWaterStorage;
            if (output != null)
            {
                LiquidContainer outputPrefab = output.LookupPrefab<LiquidContainer>();
                int configuredOutput = powered ? config.PoweredWaterPurifierOutput : config.WaterPurifierOutput;
                int outputCapacity = state && configuredOutput > 0 ? configuredOutput : outputPrefab?.maxStackSize ?? 0;
                if (outputCapacity > 0)
                {
                    SetMaxStackSize(output, outputCapacity);
                    output.MarkDirtyForceUpdateOutputs();
                }
            }

            if (prefab != null)
            {
                int processing = powered ? config.PoweredWaterPurifierProcessingPerMinute : config.WaterPurifierProcessingPerMinute;
                int ratio = powered ? config.PoweredWaterPurifierFreshWaterRatio : config.WaterPurifierFreshWaterRatio;

                purifier.waterToProcessPerMinute = state && processing > 0 ? processing : prefab.waterToProcessPerMinute;
                purifier.freshWaterRatio = state && ratio > 0 ? ratio : prefab.freshWaterRatio;
                purifier.stopWhenOutputFull = state ? (powered ? config.PoweredWaterPurifierStopWhenOutputFull : config.WaterPurifierStopWhenOutputFull) : prefab.stopWhenOutputFull;
            }
        }

        private void UpdateWaterPump(WaterPump pump, bool state)
        {
            if (pump == null) return;

            WaterPump prefab = pump.LookupPrefab<WaterPump>();
            int capacity = state && config.WaterPump.StackSize > 0 ? config.WaterPump.StackSize : prefab?.maxStackSize ?? 0;
            if (capacity > 0)
            {
                SetMaxStackSize(pump, capacity);
                SetSlotAmounts(pump, capacity);
            }

            int amount = state && config.WaterPump.Amount > 0 ? config.WaterPump.Amount : prefab?.AmountPerPump ?? 0;
            if (amount > 0) pump.AmountPerPump = amount;

            float interval = state && config.WaterPump.Interval > 0f ? config.WaterPump.Interval : prefab?.PumpInterval ?? 0f;
            if (interval > 0f && pump.PumpInterval != interval)
            {
                pump.PumpInterval = interval;
                if (pump.IsPowered())
                {
                    pump.createWaterAction ??= pump.CreateWater;
                    pump.CancelInvoke(pump.createWaterAction);
                    pump.InvokeRandomized(pump.createWaterAction, interval, interval, interval * 0.1f);
                }
            }
        }

        private void UpdateWaterWell(WaterWell well, bool state)
        {
            WaterWell prefab = well.LookupPrefab<WaterWell>();
            int capacity = state && config.WaterWell.StackSize > 0 ? config.WaterWell.StackSize : prefab?.maxStackSize ?? 0;
            if (capacity > 0)
            {
                SetMaxStackSize(well, capacity);
                SetSlotAmounts(well, capacity);
            }

            int amount = state && config.WaterWell.WaterPerPump > 0 ? config.WaterWell.WaterPerPump : prefab?.waterPerPump ?? 0;
            if (amount > 0) well.waterPerPump = amount;
        }

        #region Configuration

        private Configuration config;

        private int GetConfigValue(string shortname) => shortname switch
        {
            "bucket.water" => config.WaterBucket,
            "waterjug" => config.WaterJug,
            "pistol.water" => config.WaterPistol,
            "gun.water" => config.WaterGun,
            "smallwaterbottle" => config.SmallWaterBottle,
            "botabag" => config.BotaBag,
            _ => 0
        };

        private class SmallWaterCatcherSettings
        {
            [JsonProperty(PropertyName = "Stack Size (vanilla: 10000)")]
            public int StackSize = 50000;

            [JsonProperty(PropertyName = "Add Amount Of Water (vanilla: 10)")]
            public float MaxItemToCreate = 20f;

            [JsonProperty(PropertyName = "Add Water Every X Seconds (vanilla: 60)")]
            public float Interval = 30f;
        }

        private class LargeWaterCatcherSettings
        {
            [JsonProperty(PropertyName = "Stack Size (vanilla: 50000)")]
            public int StackSize = 250000;

            [JsonProperty(PropertyName = "Add Amount Of Water (vanilla: 30)")]
            public float MaxItemToCreate = 60f;

            [JsonProperty(PropertyName = "Add Water Every X Seconds (vanilla: 60)")]
            public float Interval = 30f;
        }

        private class WaterPumpSettings
        {
            [JsonProperty(PropertyName = "ML Capacity (vanilla: 2000)")]
            public int StackSize = 10000;

            [JsonProperty(PropertyName = "Add Amount Of Water To Water Pumps (Vanilla: 85)")]
            public int Amount = 130;

            [JsonProperty(PropertyName = "Add Water To Water Pumps Every X Seconds (Vanilla: 10)")]
            public float Interval = 10f;
        }

        private class WaterWellSettings
        {
            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled;

            [JsonProperty(PropertyName = "ML Capacity (vanilla: 5000)")]
            public int StackSize = 25000;

            [JsonProperty(PropertyName = "Water Per Pump (vanilla: 100)")]
            public int WaterPerPump = 250;
        }

        private class Configuration
        {
            [JsonProperty(PropertyName = "Small Water Catcher")]
            public SmallWaterCatcherSettings SmallWaterCatcher = new();

            [JsonProperty(PropertyName = "Large Water Catcher")]
            public LargeWaterCatcherSettings LargeWaterCatcher = new();

            [JsonProperty(PropertyName = "Water Pump")]
            public WaterPumpSettings WaterPump = new();

            [JsonProperty(PropertyName = "Water Well")]
            public WaterWellSettings WaterWell = new();

            [JsonProperty(PropertyName = "Water Jug ML Capacity (vanilla: 5000)")]
            public int WaterJug = 25000;

            [JsonProperty(PropertyName = "Water Barrel ML Capacity (vanilla: 20000)")]
            public int WaterBarrel = 100000;

            [JsonProperty(PropertyName = "Water Bucket ML Capacity (vanilla: 2000)")]
            public int WaterBucket = 10000;

            [JsonProperty(PropertyName = "Water Ground Pool Capacity (vanilla: 500)")]
            public int GroundPool = 500;

            [JsonProperty(PropertyName = "Water Big Ground Pool Capacity (vanilla: 2000)")]
            public int BigGroundPool = 2000;

            [JsonProperty(PropertyName = "Water Purifier ML Capacity (vanilla: 5000)")]
            public int WaterPurifier = 25000;

            [JsonProperty(PropertyName = "Water Purifier Fresh Water Output ML Capacity (vanilla: 2000)")]
            public int WaterPurifierOutput = 25000;

            [JsonProperty(PropertyName = "Water Purifier Processing Per Minute (vanilla: 1000)")]
            public int WaterPurifierProcessingPerMinute;

            [JsonProperty(PropertyName = "Water Purifier Fresh Water Ratio (vanilla: 4)")]
            public int WaterPurifierFreshWaterRatio;

            [JsonProperty(PropertyName = "Water Purifier Stop When Output Full")]
            public bool WaterPurifierStopWhenOutputFull;

            [JsonProperty(PropertyName = "Water Purifier (Powered) ML Capacity (vanilla: 5000)")]
            public int PoweredWaterPurifier = 25000;

            [JsonProperty(PropertyName = "Water Purifier (Powered) Fresh Water Output ML Capacity (vanilla: 5000)")]
            public int PoweredWaterPurifierOutput = 25000;

            [JsonProperty(PropertyName = "Water Purifier (Powered) Processing Per Minute (vanilla: 4000)")]
            public int PoweredWaterPurifierProcessingPerMinute;

            [JsonProperty(PropertyName = "Water Purifier (Powered) Fresh Water Ratio (vanilla: 2)")]
            public int PoweredWaterPurifierFreshWaterRatio;

            [JsonProperty(PropertyName = "Water Purifier (Powered) Stop When Output Full")]
            public bool PoweredWaterPurifierStopWhenOutputFull = true;

            [JsonProperty(PropertyName = "Water Pistol ML Capacity (vanilla: 250)")]
            public int WaterPistol = 500;

            [JsonProperty(PropertyName = "Water Gun ML Capacity (vanilla: 1000)")]
            public int WaterGun = 2000;

            [JsonProperty(PropertyName = "Small Water Bottle ML Capacity (vanilla: 250)")]
            public int SmallWaterBottle = 1000;

            [JsonProperty(PropertyName = "Bota Bag ML Capacity (default: 3000)")]
            public int BotaBag = 3000;

            [JsonProperty(PropertyName = "Reset To Vanilla Defaults On Unload")]
            public bool Reset;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            canSaveConfig = false;
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) LoadDefaultConfig();
                canSaveConfig = true;
                SaveConfig();
            }
            catch (Exception ex)
            {
                Puts(ex.ToString());
                LoadDefaultConfig();
            }
        }

        private bool canSaveConfig = true;

        protected override void SaveConfig()
        {
            if (canSaveConfig)
            {
                Config.WriteObject(config);
            }
        }

        protected override void LoadDefaultConfig() => config = new();

        #endregion

        #region Configuration Classes

        private class ItemConfig
        {
            public string Shortname;
            public int ItemId;
            public int DefaultMaxStackSize;
            public int ConfigMaxStackSize;
        }

        #endregion
    }
}