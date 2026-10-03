using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;

namespace BigChoppa;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "com.andrew1431.bigchoppa";
    public const string PluginName = "Big Choppa";
    public const string PluginVersion = "1.0.2";

    internal static ManualLogSource L;

    public override void Load()
    {
        L = Log;
        ChoppaConfig.Bind(Config);

        ClassInjector.RegisterTypeInIl2Cpp<Helicopter>();
        ClassInjector.RegisterTypeInIl2Cpp<ChoppaBonker>();
        ClassInjector.RegisterTypeInIl2Cpp<ChoppaManager>();
        AddComponent<ChoppaManager>();

        L.LogInfo($"{PluginName} {PluginVersion} loaded. Press {ChoppaConfig.SpawnKey.Value} in game to spawn a choppa.");
    }

    internal static void Verbose(string message)
    {
        if (ChoppaConfig.VerboseLogging.Value) L.LogInfo(message);
    }
}
