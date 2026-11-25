using System;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PocketLegend.BetterCardSkip
{
    /// <summary>
    /// 전투 승리 보상 화면에서 "카드 획득", "인벤토리 조각" 보상 줄마다
    /// "보상 넘기기 (+2)" 버튼을 붙인다. 누르면 그 보상을 포기하고 골드를 받는다.
    ///
    /// 게임 흐름:
    ///  - 보상 목록: Reward.GetBattleRewards → AddMoney / AddCard() / AddInventoryPiece(grade) ...
    ///  - 보상 줄 UI: RewardElementUI.Apply(reward) (줄 오브젝트는 재사용되므로 매번 버튼 표시 여부를 갱신)
    ///  - 받음 처리: reward.Get(ui) → 줄 숨김 + IsGet = true, 전부 받으면 보상 화면이 닫힘
    ///  - 카드 선택 창의 "취소"는 보상 목록으로 돌아가기일 뿐이라 골드 지급 대상이 아님
    /// </summary>
    [BepInPlugin(Guid, "Better Card Skip", "0.3.1")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "pocketlegend.bettercardskip";
        // 모드 관리(Mod Switch)가 리플렉션으로 읽는 표시 정보: 분류(UX=조작 편의, UI=정보 표시, BAL=밸런스 변경) / 한국어 이름 / 협동 플레이 미적용
        public const string ModCategory = "BAL";
        public const string NameKo = "보상 넘기기";
        public const bool SingleOnly = true;
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Gold;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "모드 사용 여부 (타이틀 화면의 모드 관리에서도 변경 가능)");
            Gold = Config.Bind("General", "SkipGold", 2, new ConfigDescription(
                "보상(카드 획득 / 인벤토리 조각)을 넘길 때 받는 골드 (보상 1개당)", new AcceptableValueRange<int>(0, 100)));
            new Harmony(Guid).PatchAll(typeof(Patches));
        }
    }

    internal static class Patches
    {
        // 넘기기 대상 보상 표시 (Reward 객체에 꼬리표를 붙이는 용도, GC 되면 자동 제거)
        private static readonly ConditionalWeakTable<Reward, object> Skippable = new ConditionalWeakTable<Reward, object>();
        private static readonly object Tag = new object();

        [HarmonyPostfix, HarmonyPatch(typeof(Reward), nameof(Reward.AddCard), new Type[0])]
        private static void Reward_AddCard(Reward __result) => Mark(__result);

        [HarmonyPostfix, HarmonyPatch(typeof(Reward), nameof(Reward.AddInventoryPiece))]
        private static void Reward_AddInventoryPiece(Reward __result) => Mark(__result);

        // 전투 승리 보상만 대상 (인벤토리 조각은 퍼즐 이벤트 Event_Puzzle 에서도 쓰이므로 구분)
        private static bool buildingBattleRewards;

        [HarmonyPrefix, HarmonyPatch(typeof(Reward), nameof(Reward.GetBattleRewards))]
        private static void GetBattleRewards_Prefix() => buildingBattleRewards = true;

        [HarmonyFinalizer, HarmonyPatch(typeof(Reward), nameof(Reward.GetBattleRewards))]
        private static void GetBattleRewards_Finalizer() => buildingBattleRewards = false;

        private static void Mark(Reward reward)
        {
            if (!buildingBattleRewards || reward == null) return;
            if (!Skippable.TryGetValue(reward, out _)) Skippable.Add(reward, Tag);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(RewardElementUI), nameof(RewardElementUI.Apply))]
        private static void RewardElementUI_Apply(RewardElementUI __instance, Reward reward)
        {
            try
            {
                // 협동 플레이 중에는 골드가 상대와 공유되므로 버튼을 만들지 않음 (상대의 게임 밸런스에 영향 주지 않기)
                bool show = Plugin.Enabled.Value && Plugin.Gold.Value > 0 && reward != null
                            && !reward.IsConnect && Skippable.TryGetValue(reward, out _) && !Coop.IsRun;
                if (Coop.IsRun && Plugin.Enabled.Value) Coop.LogOnce(Plugin.Log, "협동 플레이 중이라 '보상 넘기기' 버튼을 표시하지 않습니다.");
                SkipButton.Bind(__instance, show ? reward : null);
            }
            catch (Exception e) { Plugin.Log.LogError(e); }
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

    /// <summary>보상 줄 오른쪽에 붙는 "보상 넘기기 (+n)" 버튼</summary>
    internal static class SkipButton
    {
        private const string Name = "BetterCardSkip_Button";

        public static void Bind(RewardElementUI element, Reward reward)
        {
            var existing = element.transform.Find(Name);
            if (reward == null)
            {
                if (existing != null) existing.gameObject.SetActive(false); // 재사용되는 줄이므로 숨김
                return;
            }

            var go = existing != null ? existing.gameObject : Create(element);
            go.SetActive(true);
            int gold = Plugin.Gold.Value;
            go.GetComponentInChildren<TextMeshProUGUI>().text = $"보상 넘기기 (+{gold})";

            var button = go.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => Skip(element, reward, go.transform.position, gold));
        }

        private static void Skip(RewardElementUI element, Reward reward, Vector3 from, int gold)
        {
            if (reward.IsGet) return; // 중복 클릭 방지
            if (Coop.IsRun) return;    // 버튼이 떠 있는 사이 협동 상태가 된 경우까지 차단
            try
            {
                // 카드 보상은 "넘긴 카드" 기록을 남기도록 원래 Pass 처리 실행 (IsGet 이 false 일 때만 기록됨)
                reward.Pass?.Invoke();

                if (FXManager.Instance != null && Getable.Get<ResourceUI>() != null)
                    FXManager.Instance.AddGold(from, gold);
                else
                    PlayData.Instance.Value.playerData.gold.Value += gold;

                // 게임의 "받음" 처리: 줄 숨김 + 모두 처리되면 보상 화면 닫기
                reward.Get(element);
                Plugin.Log.LogInfo($"보상 넘김 → {gold} 골드");
            }
            catch (Exception e) { Plugin.Log.LogError(e); }
        }

        private static GameObject Create(RewardElementUI element)
        {
            var go = new GameObject(Name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
            var rt = (RectTransform)go.transform;
            rt.SetParent(element.transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(220f, 52f);
            rt.anchoredPosition = new Vector2(-24f, 0f);

            var img = go.GetComponent<Image>();
            img.color = new Color(0.10f, 0.08f, 0.06f, 0.9f);
            var outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(0.85f, 0.70f, 0.35f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);

            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.4f, 1.3f, 1.1f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = colors;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var trt = (RectTransform)textGo.transform;
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            var font = element.textUI != null ? element.textUI.font : null;
            if (font != null)
            {
                tmp.font = font;
                tmp.fontSharedMaterial = font.material;
            }
            tmp.fontSize = 24f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(1f, 0.85f, 0.45f);
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;

            go.transform.SetAsLastSibling();
            return go;
        }
    }
}
