using Newtonsoft.Json;
using System;
using System.Linq;

/*
Restructed some of the config which requires you to reconfigure it
Fixed stack size amounts and values applied to configuration defaults
*/

namespace Oxide.Plugins
{
    [Info("Water Works", "nivex", "1.0.5")]
    [Description("Control the monopoly on your water supplies.")]
    class WaterWorks : RustPlugin
    {
        private ItemModContainer bucketWaterMod;
        private ItemModContainer waterJugMod;
        private ItemDefinition waterDef;

        private void Init()
        {
            Unsubscribe(nameof(OnEntitySpawned));
        }

        private void OnServerInitialized()
        {
            bucketWaterMod = ItemManager.FindItemDefinition("bucket.water")?.GetComponent<ItemModContainer>();
            waterJugMod = ItemManager.FindItemDefinition("waterjug")?.GetComponent<ItemModContainer>();
            waterDef = ItemManager.FindItemDefinition("water");

            foreach (var lc in BaseNetworkable.serverEntities.OfType<LiquidContainer>())
            {
                SetLiquidContainerStackSize(lc, true);
            }

            ConfigureWaterDefinitions(true);
            Subscribe(nameof(OnEntitySpawned));
        }

        private void Unload()
        {
            foreach (var lc in BaseNetworkable.serverEntities.OfType<LiquidContainer>())
            {
                SetLiquidContainerStackSize(lc, false);
            }

            ConfigureWaterDefinitions(false);
        }

        private void OnEntitySpawned(LiquidContainer lc)
        {
            SetLiquidContainerStackSize(lc, true);
        }

        private void SetSlotAmounts(LiquidContainer lc, int num)
        {
            var slot = lc.inventory.GetSlot(0);

            if (slot != null && slot.amount > num)
            {
                slot.amount = num;
            }
        }

        private void SetLiquidContainerStackSize(LiquidContainer lc, bool state)
        {
            if (lc == null || lc.IsDestroyed)
            {
                return;
            }

            if (lc.prefabID == 3746060889 && _config.WaterBarrel > 0)
            {
                int num = state ? _config.WaterBarrel : 20000;

                SetSlotAmounts(lc, num);

                lc.maxStackSize = num;
                lc.inventory.maxStackSize = num;
                lc.MarkDirty();
                lc.inventory.MarkDirty();
                lc.SendNetworkUpdateImmediate();
            }
            else if (lc is WaterCatcher)
            {
                UpdateWaterCatcher(lc as WaterCatcher, state);
            }
            else if (lc.prefabID == 1259335874 && _config.PoweredWaterPurifier > 0)
            {
                int num = state ? _config.PoweredWaterPurifier : 5000;

                SetSlotAmounts(lc, num);

                lc.maxStackSize = num;
                lc.inventory.maxStackSize = num;
                lc.MarkDirty();
                lc.inventory.MarkDirty();
                lc.SendNetworkUpdateImmediate();
            }
            else if (lc.prefabID == 2905007296 && _config.WaterPurifier > 0)
            {
                var purifier = lc as WaterPurifier;
                int num = state ? _config.WaterPurifier : 5000;
                
                SetSlotAmounts(lc, num);

                purifier.stopWhenOutputFull = true;
                purifier.waterStorage.maxStackSize = num;
                purifier.maxStackSize = num;
                purifier.inventory.maxStackSize = num;
                purifier.MarkDirty();
                purifier.inventory.MarkDirty();
                purifier.SendNetworkUpdateImmediate();
            }
            else if (lc is WaterPump && _config.WaterPump.StackSize > 0)
            {
                var pump = lc as WaterPump;
                int num = state ? _config.WaterPump.StackSize : 2000;

                SetSlotAmounts(lc, num);

                pump.maxStackSize = num;
                pump.inventory.maxStackSize = num;
                pump.MarkDirty();
                pump.inventory.MarkDirty();
                pump.SendNetworkUpdateImmediate();
                UpdateWaterPump(pump, state);
            }
        }

        private void ConfigureWaterDefinitions(bool state)
        {
            if (bucketWaterMod != null) bucketWaterMod.maxStackSize = state ? _config.WaterBucket : 2000;
            if (waterJugMod != null) waterJugMod.maxStackSize = state ? _config.WaterJug : 5000;
            if (waterDef != null) waterDef.stackable = state ? _config.WaterJug * 62 : 249999;
        }

        private void UpdateWaterCatcher(WaterCatcher wc, bool state)
        {
            if (state)
            {
                wc.maxStackSize = wc.inventory.maxStackSize = wc.prefabID == 3418194637 ? _config.LargeWaterCatcher.StackSize : _config.SmallWaterCatcher.StackSize;
                wc.maxItemToCreate = wc.prefabID == 3418194637 ? _config.LargeWaterCatcher.Amount : _config.SmallWaterCatcher.Amount;
            }
            else
            {
                wc.maxStackSize = wc.inventory.maxStackSize = wc.prefabID == 3418194637 ? 50000 : 10000;
                wc.maxItemToCreate = wc.prefabID == 3418194637 ? 30 : 10;
            }

            SetSlotAmounts(wc, wc.maxStackSize);

            wc.Invoke(() =>
            {
                if (wc.IsDestroyed) return;
                wc.CancelInvoke(wc.CollectWater);

                if (state)
                {
                    float interval = wc.prefabID == 3418194637 ? _config.LargeWaterCatcher.Interval : _config.SmallWaterCatcher.Interval;
                    wc.InvokeRepeating(wc.CollectWater, interval, interval);
                }
                else wc.InvokeRandomized(wc.CollectWater, WaterCatcher.collectInterval, WaterCatcher.collectInterval, 6f);
            }, 0.1f);
        }

        private void UpdateWaterPump(WaterPump pump, bool state)
        {
            pump.PumpInterval = state ? _config.WaterPump.Interval : 10f;
            pump.AmountPerPump = state ? _config.WaterPump.Amount : 85;
            pump.CancelInvoke(pump.CreateWater);
            pump.InvokeRandomized(pump.CreateWater, pump.PumpInterval, pump.PumpInterval, pump.PumpInterval * 0.1f);
        }

        #region Configuration

        private Configuration _config;

        private class SmallWaterCatcherSettings
        {
            [JsonProperty(PropertyName = "Stack Size (vanilla: 10000)")]
            public int StackSize { get; set; } = 50000;

            [JsonProperty(PropertyName = "Add Amount Of Water (vanilla: 10)")]
            public float Amount { get; set; } = 20f;

            [JsonProperty(PropertyName = "Add Water Every X Seconds (vanilla: 60)")]
            public float Interval { get; set; } = 30f;
        }

        private class LargeWaterCatcherSettings
        {
            [JsonProperty(PropertyName = "Stack Size (vanilla: 50000)")]
            public int StackSize { get; set; } = 250000;

            [JsonProperty(PropertyName = "Add Amount Of Water (vanilla: 30)")]
            public float Amount { get; set; } = 60f;

            [JsonProperty(PropertyName = "Add Water Every X Seconds (vanilla: 60)")]
            public float Interval { get; set; } = 30f;
        }

        private class WaterPumpSettings
        {
            //[JsonProperty(PropertyName = "Max Output (vanilla: 12)")]
            //public int Output { get; set; } = 20;

            [JsonProperty(PropertyName = "ML Capacity (vanilla: 2000)")]
            public int StackSize { get; set; } = 10000;

            [JsonProperty(PropertyName = "Add Amount Of Water To Water Pumps")]
            public int Amount { get; set; } = 130;

            [JsonProperty(PropertyName = "Add Water To Water Pumps Every X Seconds")]
            public float Interval { get; set; } = 10f;
        }

        private class Configuration
        {
            [JsonProperty(PropertyName = "Small Water Catcher")]
            public SmallWaterCatcherSettings SmallWaterCatcher { get; set; } = new SmallWaterCatcherSettings();

            [JsonProperty(PropertyName = "Large Water Catcher")]
            public LargeWaterCatcherSettings LargeWaterCatcher { get; set; } = new LargeWaterCatcherSettings();

            [JsonProperty(PropertyName = "Water Pump")]
            public WaterPumpSettings WaterPump { get; set; } = new WaterPumpSettings();

            [JsonProperty(PropertyName = "Water Jug ML Capacity (vanilla: 5000)")]
            public int WaterJug { get; set; } = 25000;

            [JsonProperty(PropertyName = "Water Barrel ML Capacity (vanilla: 20000)")]
            public int WaterBarrel { get; set; } = 100000;

            [JsonProperty(PropertyName = "Water Bucket ML Capacity (vanilla: 2000)")]
            public int WaterBucket { get; set; } = 10000;

            [JsonProperty(PropertyName = "Water Purifier ML Capacity (vanilla: 5000)")]
            public int WaterPurifier { get; set; } = 25000;

            [JsonProperty(PropertyName = "Water Purifier (Powered) ML Capacity (vanilla: 5000)")]
            public int PoweredWaterPurifier { get; set; } = 25000;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();
            }
            catch
            {
                PrintError("Your configuration file contains an error. Using default configuration values.");
                LoadDefaultConfig();
            }

            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        protected override void LoadDefaultConfig() => _config = new Configuration();

        #endregion
    }
}
