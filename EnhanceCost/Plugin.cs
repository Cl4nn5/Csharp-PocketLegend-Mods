using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace PocketLegend.EnhanceCost
{
    /// <summary>
    /// 대장간 강화 비용(원래: 노말 1 / 레어 2 / 유니크 3)을 항상 강화석 1개로 만든다.
    /// 원본 코드: EnhanceUI.SetCost, EnhanceUI.Select 안의 로컬 함수 Apply
    ///   → (int)(cardData.ItemGrade + 1)
    /// </summary>
    [BepInPlugin(Guid, "Fixed Enhance Cost", "0.1.1")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "pocketlegend.enhancecost";
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> FixedCost;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "모드 사용 여부 (타이틀 화면의 모드 관리에서도 변경 가능)");
            FixedCost = Config.Bind("General", "FixedCost", 1, new ConfigDescription(
                "강화 1회에 소모되는 강화석 개수", new AcceptableValueRange<int>(0, 10)));
            new Harmony(Guid).PatchAll(typeof(CostPatch));
        }

        /// <summary>원래 식 (grade + one) 대신 호출됨</summary>
        public static int Cost(int grade, int one) => Enabled.Value ? FixedCost.Value : grade + one;
    }

    [HarmonyPatch]
    internal static class CostPatch
    {
        private static readonly MethodInfo GetItemGrade = AccessTools.PropertyGetter(typeof(CardData), nameof(CardData.ItemGrade));
        private static readonly MethodInfo CostMethod = AccessTools.Method(typeof(Plugin), nameof(Plugin.Cost));

        // EnhanceUI.SetCost + 컴파일러가 만든 로컬 함수(<Select>g__Apply|...)
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(EnhanceUI), "SetCost");
            var types = new[] { typeof(EnhanceUI) }.Concat(typeof(EnhanceUI).GetNestedTypes(AccessTools.all));
            foreach (var type in types)
                foreach (var m in type.GetMethods(AccessTools.all | BindingFlags.DeclaredOnly))
                    if (m.Name.Contains("g__Apply"))
                        yield return m;
        }

        // 패턴: callvirt CardData::get_ItemGrade / ldc.i4.1 / add  →  add 를 Plugin.Cost(grade, 1) 호출로 교체
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var codes = instructions.ToList();
            int patched = 0;
            for (int i = 0; i + 2 < codes.Count; i++)
            {
                if (codes[i].Calls(GetItemGrade) && codes[i + 1].LoadsConstant(1) && codes[i + 2].opcode == OpCodes.Add)
                {
                    codes[i + 2].opcode = OpCodes.Call;
                    codes[i + 2].operand = CostMethod;
                    patched++;
                }
            }
            if (patched == 0) Plugin.Log.LogWarning($"{original.DeclaringType?.Name}.{original.Name}: 패턴을 찾지 못함 (게임 업데이트로 코드가 바뀌었을 수 있음)");
            else Plugin.Log.LogInfo($"{original.DeclaringType?.Name}.{original.Name}: 비용 계산 {patched}곳 패치");
            return codes;
        }
    }
}
