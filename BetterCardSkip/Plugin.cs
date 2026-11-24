using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace PocketLegend.BetterCardSkip
{
    /// <summary>
    /// [시험 버전] 전투 보상에서 카드(아이템) 보상을 받지 않고 넘기면 골드를 조금 준다.
    ///
    /// 게임 흐름:
    ///  - 전투 보상 목록은 Reward.GetBattleRewards 에서 만들고, 카드 보상은 Reward.AddCard() 로 생성된다.
    ///  - 카드 선택 창(SelectItemUI)의 취소 버튼은 보상 목록으로 "돌아가기"일 뿐이라 여기서 주면 반복 획득이 가능하다.
    ///  - 보상 화면의 넘기기 버튼(RewardUI.passButtonUI)을 누르면 모든 보상의 Reward.Pass 가 호출되고,
    ///    카드 보상은 !IsGet 일 때 "넘긴 카드"로 기록된다. → 이 시점에만 1회 지급.
    /// </summary>
    [BepInPlugin(Guid, "Better Card Skip (Test)", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "pocketlegend.bettercardskip";
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Gold;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "모드 사용 여부 (타이틀 화면의 모드 관리에서도 변경 가능)");
            Gold = Config.Bind("General", "Gold", 3, new ConfigDescription(
                "카드 보상을 넘겼을 때 받는 골드", new AcceptableValueRange<int>(0, 100)));
            new Harmony(Guid).PatchAll(typeof(Patches));
        }
    }

    internal static class Patches
    {
        // 전투 보상용 카드 보상 (인자 없는 오버로드)
        [HarmonyPostfix, HarmonyPatch(typeof(Reward), nameof(Reward.AddCard), new Type[0])]
        private static void Reward_AddCard(Reward __result)
        {
            var reward = __result;
            reward.Pass = (Action)Delegate.Combine(reward.Pass, (Action)(() =>
            {
                if (!Plugin.Enabled.Value || reward.IsGet) return;
                int gold = Plugin.Gold.Value;
                if (gold <= 0 || PlayData.Instance.Value == null) return;
                GiveGold(gold);
            }));
        }

        private static void GiveGold(int gold)
        {
            try
            {
                // 넘기기 버튼 위치에서 골드가 날아가는 기존 연출(FXManager.AddGold) 사용
                var rewardUI = Getable.Get<RewardUI>();
                var fx = FXManager.Instance;
                if (fx != null && rewardUI != null && rewardUI.passButtonUI != null && Getable.Get<ResourceUI>() != null)
                    fx.AddGold(rewardUI.passButtonUI.transform.position, gold);
                else
                    PlayData.Instance.Value.playerData.gold.Value += gold;
                Plugin.Log.LogInfo($"카드 보상 넘김 → {gold} 골드 지급");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }
    }
}
