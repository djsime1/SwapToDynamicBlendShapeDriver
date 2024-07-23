using System.Reflection;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using FrooxEngine.UIX;
using HarmonyLib;
using ResoniteModLoader;

namespace SwapToDynamicBlendShapeDriver;

public class SwapToDynamicBlendShapeDriver : ResoniteMod
{
    public override string Name => "SwapToDynamicBlendShapeDriver";
    public override string Author => "djsime1 / Zenuru";
    public override string Version => "1.0.0";
    public override string Link => "https://github.com/djsime1/SwapToDynamicBlendShapeDriver";

    public static ModConfiguration? Config;

    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> enable = new("Enable", "Enable/disable generating buttons under SkinnedMeshRenderer components", () => true);
    public static bool Config_Enable => Config!.GetValue(enable);

    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> skipCommonDrivers = new("SkipCommonDrivers", "Ignore blendshapes driven by common viseme & face/eye tracking components", () => true);
    public static bool Config_SkipCommonDrivers => Config!.GetValue(skipCommonDrivers);

    public override void OnEngineInit()
    {
        Config = GetConfiguration();
        var harmony = new Harmony("je.dj.SwapToDynamicBlendShapeDriver");
        harmony.PatchAll();
        Config!.Save(true);
    }

    [HarmonyPatch(typeof(SkinnedMeshRenderer), "BuildInspectorUI")]
    class SwapToDynamicBlendShapeDriverPatch
    {
        public static void Postfix(SkinnedMeshRenderer __instance, UIBuilder ui)
        {
            if (!Config_Enable) return;

            ui.Style.MinHeight = 36;
            ui.Text("SwapToDynamicBlendShapeDriver");
            ui.Style.MinHeight = 24;

            var newDBSD = ui.Button("Move driven blendshapes into new DynamicBlendShapeDriver");
            var mergeDBSD = ui.Button("Add driven blendshapes into existing DynamicBlendShapeDriver");

            newDBSD.LocalPressed += (btn, data) => {
                var DBSD = __instance.Slot.AttachComponent<DynamicBlendShapeDriver>();
                DBSD.Renderer.Target = __instance;
                var outcome = MigrateDrivenFields(__instance, DBSD);
                Inform(btn, outcome ? "Created new DynamicBlendShapeDriver" : "No drivers were moved");
                if (!outcome) DBSD.Destroy();
            };

            mergeDBSD.LocalPressed += (btn, data) => {
                DynamicBlendShapeDriver? target = null;
                try {
                    target = __instance.Slot.GetComponents<DynamicBlendShapeDriver>().First((c) => c.Renderer.Target == __instance);
                }
                catch (InvalidOperationException) {} // No DBSD found
                if (target is null) {
                    Inform(btn, "Couldn't find suitable DynamicBlendShapeDriver on this slot");
                } else {
                    var outcome = MigrateDrivenFields(__instance, target);
                    Inform(btn, outcome ? "Found a DynamicBlendShapeDriver, moved driven blendshapes" : "No drivers were moved");
                }
            };
        }

        private static Type[] skipComponents = [
            typeof(DynamicBlendShapeDriver),
            typeof(DirectVisemeDriver),
            typeof(DynamicVisemeDriver),
            typeof(AvatarExpressionDriver),
            typeof(EyeLinearDriver),
        ];

        private static MethodInfo UpdateBlendShapes = AccessTools.DeclaredMethod(typeof(DynamicBlendShapeDriver), "UpdateBlendShapes");

        private static bool MigrateDrivenFields(SkinnedMeshRenderer source, DynamicBlendShapeDriver target) {
            if (target.Renderer.Target != source) return false;

            var movedAnything = false;
            for (int i = 0; i < source.BlendShapeWeights.Count; i++)
            {
                var field = source.BlendShapeWeights.GetField(i);
                if (!field.IsDriven) continue;

                var hook = field.ActiveLink as FieldDrive<float>;
                var driver = hook.FindNearestParent<Component>();

                if (driver is DynamicBlendShapeDriver) continue;
                if (Config_SkipCommonDrivers && skipComponents.Contains(driver.GetType())) continue;

                var destination = target.BlendShapes.Add();
                destination.BlendShapeName.Value = source.BlendShapeName(i);
                hook!.ForceLink(destination.Value);
                movedAnything = true;
            }

            UpdateBlendShapes.Invoke(target, null);

            return movedAnything;
        }

        private static void Inform(IButton btn, string newMessage) {
            var oldMessage = btn.LabelText;
            btn.LabelText = newMessage;
            btn.Enabled = false;
            btn.RunInSeconds(3, delegate {
                btn.LabelText = oldMessage;
                btn.Enabled = true;
            });
        }
    }
}