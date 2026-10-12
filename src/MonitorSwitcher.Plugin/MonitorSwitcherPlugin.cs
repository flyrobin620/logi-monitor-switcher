namespace Loupedeck.MonitorSwitcherPlugin
{
    public sealed class MonitorSwitcherPlugin : Plugin
    {
        public override bool UsesApplicationApiOnly => true;
        public override bool HasNoApplication => true;

        public override void Load()
        {
            Log.Info("Monitor Switcher loaded.");
        }

        public override void Unload()
        {
            Log.Info("Monitor Switcher unloaded.");
        }
    }

    public sealed class MonitorSwitcherApplication : ClientApplication
    {
    }
}
