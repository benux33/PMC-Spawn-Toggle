using System;
using BepInEx.Configuration;

namespace PmcSpawnToggle.Client
{
    // Configuration Manager discovers these members by name. Keeping the small
    // compatibility class here avoids depending on one particular F12 build.
    internal sealed class ConfigurationManagerAttributes
    {
        public Action<ConfigEntryBase> CustomDrawer;
        public bool? HideDefaultButton;
        public int? Order;
    }
}
