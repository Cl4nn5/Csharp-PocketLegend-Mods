using System;
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
    [BepInPlugin(Guid, "Fixed Enhance Cost", "0.2.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "pocketlegend.enhancecost";
        // 모드 관리(Mod Switch)가 리플렉션으로 읽는 표시 정보: 분류(UX=조작 편의, UI=정보 표시, BAL=밸런스 변경) / 한국어 이름 / 협동 플레이 미적용
        public const string ModCategory = "BAL";
        public const string NameKo = "강화 비용 고정";
        public const bool SingleOnly = true;
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

        /// <summary>
        /// 원래 식 (grade + one) 대신 호출됨.
        /// 협동 플레이 중에는 강화석이 상대와 공유되므로 원래 비용을 그대로 사용 (상대의 게임 밸런스에 영향 주지 않기)
        /// </summary>
        public static int Cost(int grade, int one)
        {
            if (!Enabled.Value) return grade + one;
            if (Coop.IsRun)
            {
                Coop.LogOnce(Log, "협동 플레이 중이라 원래 강화 비용을 사용합니다.");
                return grade + one;
            }
            return FixedCost.Value;
        }
    }

    /// <summary>
    /// 협동 플레이 판 진행 여부 (CoopSession.IsRun).
    /// 게임 빌드 26896844 에서 추가된 클래스라, 구버전에서도 죽지 않도록 리플렉션으로 찾고 없으면 false.
    /// </summary>
    internal static class Coop
    {
        private static readonly Func<bool> getter = Create();
        private static bool logged;

        private static Func<bool> Create()
        {
            var type = AccessTools.TypeByName("CoopSession");
            var method = type != null ? AccessTools.PropertyGetter(type, "IsRun") : null;
            if (method == null) return () => false;
            return (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), method);
        }

        public static bool IsRun
        {
            get { try { return getter(); } catch { return false; } }
        }

        public static void LogOnce(ManualLogSource log, string message)
        {
            if (logged) return;
            logged = true;
            log.LogInfo(message);
        }
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
