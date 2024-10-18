using Newtonsoft.Json;
using Oxide.Core;
using System;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Water Works", "nivex", "1.0.8")]
    [Description("Control the monopoly on your water supplies.")]
    class WaterWorks : RustPlugin
    {
        private List<string> _modShortnames = new() { "bucket.water", "waterjug", "pistol.water", "gun.water", "smallwaterbottle" };
        private Dictionary<string, ItemModContainer> _modContainers = new();
        private Dictionary<ItemModContainer, int> _defaultModValues = new();
        private Dictionary<uint, float> _defaultFloatValues = new();
        private Dictionary<uint, int> _defaultIntValues = new();

        private void Init()
        {
            Unsubscribe(nameof(OnEntitySpawned));
        }

        private void OnServerInitialized()
        {
            foreach (var shortname in _modShortnames)
            {
                ItemDefinition def = ItemManager.FindItemDefinition(shortname);
                if (def != null && def.TryGetComponent<ItemModContainer>(out var container))
                {
                    _defaultModValues[container] = container.maxStackSize;
                    _modContainers[shortname] = container;
                }
            }

            SetLiquidContainerStackSize(true);
            ConfigureWaterDefinitions(true);
            Subscribe(nameof(OnEntitySpawned));
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

        private void SetSlotAmounts(LiquidContainer lc, int num)
        {
            Item slot = lc.inventory.GetSlot(0);

            if (slot != null && slot.amount > num)
            {
                slot.amount = num;
            }
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

            if (!_defaultIntValues.TryGetValue(lc.prefabID, out var defaultValue))
            {
                _defaultIntValues[lc.prefabID] = defaultValue = lc.maxStackSize;
            }

            switch (lc)
            {
                case { ShortPrefabName: "waterbarrel" }:
                    UpdateWaterBarrel(lc, state, defaultValue);
                    break;

                case WaterCatcher wc:
                    UpdateWaterCatcher(wc, state, defaultValue);
                    break;

                case { ShortPrefabName: "poweredwaterpurifier.deployed" }:
                    UpdatePoweredWaterPurifier(lc, state, defaultValue);
                    break;

                case { ShortPrefabName: "waterpurifier.deployed" }:
                    UpdateWaterPurifier(lc, state, defaultValue);
                    break;

                case WaterPump pump:
                    UpdateWaterPump(pump, state, defaultValue);
                    break;

                case { ShortPrefabName: "paddlingpool.deployed" }:
                    UpdateGroundPool(lc, state, defaultValue);
                    break;

                case { ShortPrefabName: "abovegroundpool.deployed" }:
                    UpdateBigGroundPool(lc, state, defaultValue);
                    break;

                default:
                    break;
            }
        }

        private static void SetMaxStackSize(LiquidContainer lc, int num)
        {
            lc.maxStackSize = num;
            lc.inventory.maxStackSize = num;
            lc.inventory.MarkDirty();
            lc.MarkDirtyForceUpdateOutputs();
            lc.SendNetworkUpdateImmediate();
        }

        private void ConfigureWaterDefinitions(bool state)
        {
            foreach (var (shortname, container) in _modContainers)
            {
                if (state)
                {
                    int configValue = GetConfigValue(shortname);
                    if (configValue > 0)
                    {
                        container.maxStackSize = configValue;
                    }
                }
                else if (_defaultModValues.TryGetValue(container, out int defaultValue))
                {
                    container.maxStackSize = defaultValue;
                }
            }
        }

        private void UpdateWaterBarrel(LiquidContainer lc, bool state, int defaultValue)
        {
            if (config.WaterBarrel > 0)
            {
                int num = state ? config.WaterBarrel : defaultValue;

                SetSlotAmounts(lc, num);

                SetMaxStackSize(lc, num);
            }
        }

        private void UpdateWaterCatcher(WaterCatcher wc, bool state, int defaultSizeValue)
        {
            if (!_defaultFloatValues.TryGetValue(wc.prefabID, out float defaultMaxValue))
            {
                _defaultFloatValues[wc.prefabID] = defaultMaxValue = wc.maxItemToCreate;
            }

            if (state)
            {
                int stackSize = wc.ShortPrefabName == "water_catcher_large" ? config.LargeWaterCatcher.StackSize : config.SmallWaterCatcher.StackSize;
                if (stackSize > 0)
                {
                    wc.maxStackSize = wc.inventory.maxStackSize = stackSize;
                }
                float maxItemToCreate = wc.ShortPrefabName == "water_catcher_large" ? config.LargeWaterCatcher.MaxItemToCreate : config.SmallWaterCatcher.MaxItemToCreate;
                if (maxItemToCreate > 0)
                {
                    wc.maxItemToCreate = maxItemToCreate; // these multipliers will alter this amount: baseRate (0.25), fogRate (2), rainRate (1), snowRate (0.5)
                }
            }
            else
            {
                wc.maxStackSize = wc.inventory.maxStackSize = defaultSizeValue;
                wc.maxItemToCreate = defaultMaxValue;
            }

            SetSlotAmounts(wc, wc.maxStackSize);

            float interval = wc.ShortPrefabName == "water_catcher_large" ? config.LargeWaterCatcher.Interval : config.SmallWaterCatcher.Interval;

            if (interval > 0)
            {
                wc.Invoke(() =>
                {
                    if (wc.IsDestroyed) return;
                    wc.CancelInvoke(wc.CollectWater);

                    if (state)
                    {
                        wc.InvokeRepeating(wc.CollectWater, interval, interval);
                    }
                    else wc.InvokeRandomized(wc.CollectWater, WaterCatcher.collectInterval, WaterCatcher.collectInterval, 6f);
                }, 0.1f);
            }
        }

        private void UpdatePoweredWaterPurifier(LiquidContainer lc, bool state, int defaultValue)
        {
            if (config.PoweredWaterPurifier > 0)
            {
                int num = state ? config.PoweredWaterPurifier : defaultValue;

                SetSlotAmounts(lc, num);

                SetMaxStackSize(lc, num);
            }
        }

        private void UpdateWaterPurifier(LiquidContainer lc, bool state, int defaultValue)
        {
            if (config.WaterPurifier > 0 && lc is WaterPurifier purifier)
            {
                int num = state ? config.WaterPurifier : defaultValue;

                SetSlotAmounts(lc, num);

                purifier.stopWhenOutputFull = true;
                purifier.waterStorage.maxStackSize = num;
                purifier.waterStorage.MarkDirtyForceUpdateOutputs();

                SetMaxStackSize(lc, num);
            }
        }

        private int defaultAmountPerPump;

        private void UpdateWaterPump(WaterPump pump, bool state, int defaultValue)
        {
            if (config.WaterPump.StackSize > 0)
            {
                if (!_defaultFloatValues.TryGetValue(pump.prefabID, out float defaultPumpValue))
                {
                    _defaultFloatValues[pump.prefabID] = defaultPumpValue = pump.PumpInterval;
                }

                if (defaultAmountPerPump == 0)
                {
                    defaultAmountPerPump = pump.AmountPerPump;
                }

                int num = state ? config.WaterPump.StackSize : defaultValue;

                SetSlotAmounts(pump, num);

                SetMaxStackSize(pump, num);

                pump.PumpInterval = state ? config.WaterPump.Interval : defaultPumpValue;
                pump.AmountPerPump = state ? config.WaterPump.Amount : defaultAmountPerPump;

                if (!pump.IsPowered())
                {
                    pump.CancelInvoke(pump.CreateWater);
                    return;
                }

                pump.CancelInvoke(pump.CreateWater);
                pump.InvokeRandomized(pump.CreateWater, pump.PumpInterval, pump.PumpInterval, pump.PumpInterval * 0.1f);
            }
        }

        private void UpdateGroundPool(LiquidContainer lc, bool state, int defaultValue)
        {
            if (config.GroundPool > 0)
            {
                int num = state ? config.GroundPool : defaultValue;

                SetSlotAmounts(lc, num);

                SetMaxStackSize(lc, num);
            }
        }

        private void UpdateBigGroundPool(LiquidContainer lc, bool state, int defaultValue)
        {
            if (config.BigGroundPool > 0)
            {
                int num = state ? config.BigGroundPool : defaultValue;

                SetSlotAmounts(lc, num);

                SetMaxStackSize(lc, num);
            }
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
            _ => 0
        };

        private class SmallWaterCatcherSettings
        {
            [JsonProperty(PropertyName = "Stack Size (vanilla: 10000)")]
            public int StackSize { get; set; } = 50000;

            [JsonProperty(PropertyName = "Add Amount Of Water (vanilla: 10)")]
            public float MaxItemToCreate { get; set; } = 20f;

            [JsonProperty(PropertyName = "Add Water Every X Seconds (vanilla: 60)")]
            public float Interval { get; set; } = 30f;
        }

        private class LargeWaterCatcherSettings
        {
            [JsonProperty(PropertyName = "Stack Size (vanilla: 50000)")]
            public int StackSize { get; set; } = 250000;

            [JsonProperty(PropertyName = "Add Amount Of Water (vanilla: 30)")]
            public float MaxItemToCreate { get; set; } = 60f;

            [JsonProperty(PropertyName = "Add Water Every X Seconds (vanilla: 60)")]
            public float Interval { get; set; } = 30f;
        }

        private class WaterPumpSettings
        {
            [JsonProperty(PropertyName = "ML Capacity (vanilla: 2000)")]
            public int StackSize { get; set; } = 10000;

            [JsonProperty(PropertyName = "Add Amount Of Water To Water Pumps (Vanilla: 85)")]
            public int Amount { get; set; } = 130;

            [JsonProperty(PropertyName = "Add Water To Water Pumps Every X Seconds (Vanilla: 10)")]
            public float Interval { get; set; } = 10f;
        }

        private class Configuration
        {
            [JsonProperty(PropertyName = "Small Water Catcher")]
            public SmallWaterCatcherSettings SmallWaterCatcher { get; set; } = new();

            [JsonProperty(PropertyName = "Large Water Catcher")]
            public LargeWaterCatcherSettings LargeWaterCatcher { get; set; } = new();

            [JsonProperty(PropertyName = "Water Pump")]
            public WaterPumpSettings WaterPump { get; set; } = new();

            [JsonProperty(PropertyName = "Water Jug ML Capacity (vanilla: 5000)")]
            public int WaterJug { get; set; } = 25000;

            [JsonProperty(PropertyName = "Water Barrel ML Capacity (vanilla: 20000)")]
            public int WaterBarrel { get; set; } = 100000;

            [JsonProperty(PropertyName = "Water Bucket ML Capacity (vanilla: 2000)")]
            public int WaterBucket { get; set; } = 10000;

            [JsonProperty(PropertyName = "Water Ground Pool Capacity (vanilla: 500)")]
            public int GroundPool { get; set; } = 100000;

            [JsonProperty(PropertyName = "Water Big Ground Pool Capacity (vanilla: 2000)")]
            public int BigGroundPool { get; set; } = 200000;

            [JsonProperty(PropertyName = "Water Purifier ML Capacity (vanilla: 5000)")]
            public int WaterPurifier { get; set; } = 25000;

            [JsonProperty(PropertyName = "Water Purifier (Powered) ML Capacity (vanilla: 5000)")]
            public int PoweredWaterPurifier { get; set; } = 25000;

            [JsonProperty(PropertyName = "Water Pistol ML Capacity (vanilla: 250)")]
            public int WaterPistol { get; set; } = 500;

            [JsonProperty(PropertyName = "Water Gun ML Capacity (vanilla: 1000)")]
            public int WaterGun { get; set; } = 2000;

            [JsonProperty(PropertyName = "Small Water Bottle ML Capacity (vanilla: 250)")]
            public int SmallWaterBottle { get; set; } = 1000;

            [JsonProperty(PropertyName = "Reset To Vanilla Defaults On Unload")]
            public bool Reset { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

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

        private bool canSaveConfig;

        protected override void SaveConfig()
        {
            if (canSaveConfig)
            {
                Config.WriteObject(config);
            }
        }

        protected override void LoadDefaultConfig() => config = new();

        #endregion
    }
}
