using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/*
Fixed water purifier capacity not using config value
Fixed water barrel capacity not using config value
Added `Water Pump ML Capacity (vanilla: 2000)` (10000)
Added `Add Amount Of Water To Water Pumps` (30)
Added `Add Water To Water Pumps Every X Seconds` (20)
Added `Water Purifier (Powered) ML Capacity (vanilla: 5000)` (25000)
Updated `Water Jug ML Capacity` default values
*/

namespace Oxide.Plugins
{
    [Info("Water Works", "nivex", "1.0.3")]
    [Description("Control the monopoly on your water supplies.")]
    class WaterWorks : RustPlugin
    {
        private Dictionary<WaterCatcher, Action> actions = new Dictionary<WaterCatcher, Action>();
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

        private void SetLiquidContainerStackSize(LiquidContainer lc, bool state)
        {
            if (lc?.prefabID == 3746060889 && _config.WaterBarrel > 0)
            {
                int num = state ? _config.WaterBarrel : 20000;

                lc.maxStackSize = num;
                lc.inventory.maxStackSize = num;
                lc.MarkDirty();
                lc.inventory.MarkDirty();
                lc.SendNetworkUpdateImmediate();
            }
            else if (lc is WaterCatcher && _config.WaterCatcherInterval > 0)
            {
                UpdateWaterCatcher(lc as WaterCatcher, state);
            }
            else if (lc?.prefabID == 1259335874 && _config.PoweredWaterPurifier > 0)
            {
                int num = state ? _config.PoweredWaterPurifier : 5000;

                lc.maxStackSize = num;
                lc.inventory.maxStackSize = num;
                lc.MarkDirty();
                lc.inventory.MarkDirty();
                lc.SendNetworkUpdateImmediate();
            }
            else if (lc?.prefabID == 2905007296 && _config.WaterPurifier > 0)
            {
                var purifier = lc as WaterPurifier;
                int num = state ? _config.WaterPurifier : 5000;

                purifier.waterStorage.maxStackSize = num;
                purifier.maxStackSize = num;
                purifier.inventory.maxStackSize = num;
                purifier.MarkDirty();
                purifier.inventory.MarkDirty();
                purifier.SendNetworkUpdateImmediate();
            }
            else if (lc is WaterPump && _config.WaterPump > 0)
            {
                var pump = lc as WaterPump;
                int num = state ? _config.WaterPump : 2000;

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
            if (waterJugMod != null) waterJugMod.maxStackSize = state ? _config.WaterJug : 4000;
            if (waterDef != null) waterDef.stackable = state ? _config.WaterJug * 62 : 249999;
        }

        private void UpdateWaterCatcher(WaterCatcher wc, bool state)
        {
            Action action;

            if (state)
            {
                if (wc == null || wc.IsDestroyed || actions.ContainsKey(wc))
                {
                    return;
                }

                actions[wc] = action = new Action(() => AddResource(wc));

                wc.CancelInvoke(wc.CollectWater);
                wc.InvokeRepeating(action, _config.WaterCatcherInterval, _config.WaterCatcherInterval);
                wc.inventory.maxStackSize = wc.prefabID == 3418194637 ? _config.StackSizeLarge : _config.StackSizeSmall;
            }
            else if (actions.TryGetValue(wc, out action))
            {
                wc.CancelInvoke(action);
                wc.InvokeRandomized(wc.CollectWater, 60f, 60f, 6f);
                wc.maxStackSize = wc.prefabID == 3418194637 ? 50000 : 10000;
            }
        }

        private void UpdateWaterPump(WaterPump pump, bool state)
        {
            pump.PumpInterval = state ? _config.WaterPumpInterval : 20f;
            pump.AmountPerPump = state ? _config.WaterPumpAmount : 30;
            //pump.InvokeRandomized("CreateWater", pump.PumpInterval, pump.PumpInterval, pump.PumpInterval * 0.1f);
        }

        private void AddResource(WaterCatcher wc)
        {
            if (wc.IsFull())
            {
                return;
            }

            float num = 0.25f;
            num += Climate.GetFog(wc.transform.position) * 2f;

            if (wc.TestIsOutside())
            {
                num += Climate.GetRain(wc.transform.position);
                num += Climate.GetSnow(wc.transform.position) * 0.5f;
            }

            wc.AddResource(Mathf.CeilToInt(_config.WaterCatcherAmount * num));
        }

        #region Configuration

        private Configuration _config;

        private class Configuration
        {
            [JsonProperty(PropertyName = "Water Jug ML Capacity (vanilla: 5000)")]
            public int WaterJug { get; set; } = 25000;

            [JsonProperty(PropertyName = "Water Barrel ML Capacity (vanilla: 20000)")]
            public int WaterBarrel { get; set; } = 100000;

            [JsonProperty(PropertyName = "Water Bucket ML Capacity (vanilla: 2000)")]
            public int WaterBucket { get; set; } = 10000;

            [JsonProperty(PropertyName = "Water Pump ML Capacity (vanilla: 2000)")]
            public int WaterPump { get; set; } = 10000;

            [JsonProperty(PropertyName = "Add Amount Of Water To Water Pumps")]
            public int WaterPumpAmount { get; set; } = 30;

            [JsonProperty(PropertyName = "Add Water To Water Pumps Every X Seconds")]
            public float WaterPumpInterval { get; set; } = 20f;

            [JsonProperty(PropertyName = "Water Purifier ML Capacity (vanilla: 5000)")]
            public int WaterPurifier { get; set; } = 25000;

            [JsonProperty(PropertyName = "Water Purifier (Powered) ML Capacity (vanilla: 5000)")]
            public int PoweredWaterPurifier { get; set; } = 25000;

            [JsonProperty(PropertyName = "Large WaterCatcher Stack Size (vanilla: 50000)")]
            public int StackSizeLarge { get; set; } = 250000;

            [JsonProperty(PropertyName = "Small WaterCatcher Stack Size (vanilla: 10000)")]
            public int StackSizeSmall { get; set; } = 50000;

            [JsonProperty(PropertyName = "Add Amount Of Water To Water Catchers")]
            public float WaterCatcherAmount { get; set; } = 10f;

            [JsonProperty(PropertyName = "Add Water To Water Catchers Every X Seconds")]
            public float WaterCatcherInterval { get; set; } = 20f;
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
