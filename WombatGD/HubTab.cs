// Settings hub tab (copied from mod-template snippets/generic/HubTab.cs.txt; contract in oth-settings-hub).
// HubPresent: an enabled hub runs, so skip our own menu key. Register: adds our tab (main thread).

using System;
using System.Collections.Generic;

namespace WombatGD
{
    internal static class HubTab
    {
        private const string HubKey = "WWTHP.SettingsHub.Hub.v1";
        private const string TabsKey = "WWTHP.SettingsHub.Tabs.v1";

        /// <summary>True while an enabled settings hub is loaded: skip the mod's own menu key.</summary>
        public static bool HubPresent => AppDomain.CurrentDomain.GetData(HubKey) != null;

        public static void Register(string name, Func<float, float> draw, string[] categories = null,
            Action opened = null, Action closed = null, Action reset = null, int order = 100, string owner = null)
        {
            var domain = AppDomain.CurrentDomain;
            if (domain.GetData(TabsKey) is not List<Dictionary<string, object>> tabs)
            {
                tabs = new List<Dictionary<string, object>>();
                domain.SetData(TabsKey, tabs);
            }
            var reg = new Dictionary<string, object> { ["name"] = name, ["draw"] = draw, ["order"] = order };
            if (categories != null) reg["categories"] = categories;
            if (opened != null) reg["opened"] = opened;
            if (closed != null) reg["closed"] = closed;
            if (reset != null) reg["reset"] = reset;
            if (owner != null) reg["owner"] = owner;
            tabs.Add(reg);
        }
    }
}
