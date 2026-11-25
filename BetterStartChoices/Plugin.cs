using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace PocketLegend.BetterStartChoices
{
    /// <summary>
    /// 게임 시작 이벤트(Event_Start)의 선택지를 강화한다.
    ///  1. 네 번째 선택지 "무작위 유물 받기" 추가: 가장 낮은 등급인 일반 유물(RelicType.Normal) 중 무작위 1개
    ///     (받을 수 있는 일반 유물이 없으면 받을 수 있는 모든 유물 중에서)
    ///  2. 기존 "아이템 보상"(2번)과 "인벤토리 조각"(3번)의 등급을 일반 → 희귀로 한 단계 올림
    ///
    /// 게임 흐름:
    ///  - EventUI 가 eventBehavior.SelectionChange 에 "선택지 목록을 화면에 그리는 함수"를 넣고 Apply() 호출
    ///  - Event_Start.Apply: GetRandomCardList()로 아이템 3개를 미리 뽑은 뒤 SelectionChange(선택지 3개) 호출
    ///  - 그래서 Apply 실행 중에만 SelectionChange 를 감싸서 목록을 고친 뒤 원래 함수로 넘긴다.
    /// </summary>
    [BepInPlugin(Guid, "Better Start Choices", "0.1.2")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "pocketlegend.betterstartchoices";
        // 모드 관리(Mod Switch)가 리플렉션으로 읽는 표시 정보: 분류(UX=조작 편의, UI=정보 표시, BAL=밸런스 변경) / 한국어 이름 / 협동 플레이 미적용
        public const string ModCategory = "BAL";
        public const string NameKo = "시작 선택지 강화";
        // 협동 플레이 중에는 자동으로 적용되지 않음 (모드 관리에 [싱글 전용] 표시).
        // 밸런스에 영향을 주므로 배포판에서는 차단 (0.1.0 에서는 협동에서도 동작을 확인하려고 false 였음).
        // 개발사 동의를 받으면 false 로 바꿔 협동에서도 허용할 수 있다.
        public const bool SingleOnly = true;
        public const string MenuOptions = "Options/RelicChoice;Options/UpgradeRewards";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> RelicChoice;
        internal static ConfigEntry<bool> UpgradeRewards;

        // const 를 직접 if 에 쓰면 "도달할 수 없는 코드" 경고가 나서 static 필드로 한 번 옮겨 씀
        private static readonly bool BlockInCoop = SingleOnly;

        /// <summary>지금 이 모드를 적용해도 되는지 (꺼져 있거나, 싱글 전용인데 협동 플레이 중이면 false)</summary>
        internal static bool Active => Enabled.Value && !(BlockInCoop && Coop.IsRun);

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "모드 사용 여부 (타이틀 화면의 모드 관리에서도 변경 가능)");
            RelicChoice = Config.Bind("Options", "RelicChoice", true, "시작 선택지에 '무작위 유물 받기' 추가");
            UpgradeRewards = Config.Bind("Options", "UpgradeRewards", true, "시작 선택지의 아이템 보상 / 인벤토리 조각 등급을 일반 → 희귀로");
            new Harmony(Guid).PatchAll(typeof(Patches));
        }

        /// <summary>모드 관리의 세부 옵션 줄에 보일 문구 (value 가 null 이면 항목 이름)</summary>
        public static string MenuText(string key, string value, bool korean)
        {
            if (value != null) return null; // bool 항목이라 값 문구는 모드 관리의 ON/OFF 사용
            switch (key)
            {
                case "RelicChoice": return korean ? "무작위 유물 선택지" : "Random relic choice";
                case "UpgradeRewards": return korean ? "보상 등급 희귀로" : "Rare-grade rewards";
                default: return null;
            }
        }

        internal static bool IsKorean
        {
            get
            {
                try { return Option.Instance?.languageKey == "KOR"; }
                catch { return Application.systemLanguage == SystemLanguage.Korean; }
            }
        }

        internal static string L(string ko, string en) => IsKorean ? ko : en;
    }

    internal static class Patches
    {
        // Event_Start.Apply 실행 중인지 (이때 뽑히는 아이템 목록만 희귀로 올림)
        internal static bool InStartApply;
        // "인벤토리 조각" 선택지를 누른 순간인지 (이때 열리는 조각만 희귀로 올림)
        internal static bool InStartInventory;
        private static Action<List<EventSelect>> savedSelectionChange;

        [HarmonyPrefix, HarmonyPatch(typeof(Event_Start), nameof(Event_Start.Apply))]
        private static void Apply_Prefix(Event_Start __instance)
        {
            savedSelectionChange = null;
            if (!Plugin.Active) return;
            var original = __instance.SelectionChange;
            if (original == null) return;

            InStartApply = true;
            savedSelectionChange = original;
            __instance.SelectionChange = list =>
            {
                try { StartChoices.Modify(__instance, list, original); }
                catch (Exception e) { Plugin.Log.LogError(e); }
                original(list);
            };
        }

        // Finalizer: 원래 메서드가 예외로 끝나도 반드시 실행됨 → 감쌌던 SelectionChange 를 원래대로 돌려놓음
        [HarmonyFinalizer, HarmonyPatch(typeof(Event_Start), nameof(Event_Start.Apply))]
        private static void Apply_Finalizer(Event_Start __instance)
        {
            InStartApply = false;
            if (savedSelectionChange != null) __instance.SelectionChange = savedSelectionChange;
            savedSelectionChange = null;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(SelectItemUI), nameof(SelectItemUI.GetRandomCardList))]
        private static void GetRandomCardList_Postfix(List<CardData> __result)
        {
            if (!InStartApply || !Plugin.UpgradeRewards.Value || __result == null) return;
            foreach (var card in __result)
            {
                try { if (card.ItemGrade < ItemGrade.Rare) card.Enhance(ItemGrade.Rare); } // 강화 1회 = 희귀
                catch (Exception e) { Plugin.Log.LogError(e); }
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(InventoryHandUI), nameof(InventoryHandUI.InventoryPieceOpen),
            new[] { typeof(ItemGrade), typeof(Action), typeof(Action) })]
        private static void InventoryPieceOpen_Prefix(ref ItemGrade itemGrade)
        {
            if (InStartInventory && itemGrade < ItemGrade.Rare) itemGrade = ItemGrade.Rare;
        }
    }

    internal static class StartChoices
    {
        private const string ItemIcon = "UI_Event_Icon_UniqueReward";
        private const string InventoryIcon = "UI_Event_Icon_Inventory";

        private static string CharacterKey(Event_Start ev) => $"{ev.Key}_{PlayData.Instance.Value.characterType}";

        public static void Modify(Event_Start ev, List<EventSelect> list, Action<List<EventSelect>> showSelections)
        {
            if (Plugin.UpgradeRewards.Value)
            {
                // 아이콘 이름으로 찾고, 못 찾으면 원래 순서(골드 / 아이템 / 인벤토리)로
                int itemIndex = FindByIcon(list, ItemIcon, 1);
                int inventoryIndex = FindByIcon(list, InventoryIcon, 2);
                string rare = ItemGrade.Rare.GetMessageText().SetColor(ItemGrade.Rare.Color());

                if (itemIndex >= 0)
                    list[itemIndex].SubText = () => Option.Instance.GetMessageText(CharacterKey(ev) + "_Select2_Result", rare);

                if (inventoryIndex >= 0)
                {
                    var select = list[inventoryIndex];
                    var originalSelect = select.Select;
                    select.SubText = () => Option.Instance.GetMessageText(CharacterKey(ev) + "_Select3_Result", rare);
                    select.Select = ui =>
                    {
                        Patches.InStartInventory = true;
                        try { originalSelect?.Invoke(ui); }
                        finally { Patches.InStartInventory = false; }
                    };
                }
            }

            if (Plugin.RelicChoice.Value && Candidates().Count > 0)
            {
                list.Add(new EventSelect
                {
                    Text = () => Plugin.L("쓸만한 유물이 하나 있으면 좋겠어.", "A relic would come in handy on this journey."),
                    SubText = () => Plugin.L("무작위 ", "Gain a random ")
                                    + Plugin.L("일반 유물", "Normal Relic").SetColor(ColorType.Relic)
                                    + Plugin.L(" 획득", ""),
                    Symbol = AssetManager.GetSprite("UI_Event_Icon_Relic"),
                    Select = ui => GiveRelic(ev, ui, showSelections)
                });
            }
        }

        private static int FindByIcon(List<EventSelect> list, string icon, int fallbackIndex)
        {
            int index = list.FindIndex(s => s.Symbol != null && s.Symbol.name == icon);
            if (index >= 0) return index;
            return list.Count == 3 ? fallbackIndex : -1;
        }

        /// <summary>가장 낮은 등급(일반) 유물 후보. 없으면 받을 수 있는 모든 유물</summary>
        private static List<RelicBehavior> Candidates()
        {
            var all = PlayData.Instance.Value.PossibleGetRelics;
            var normal = all.Where(r => r.RelicType == RelicType.Normal).ToList();
            return normal.Count > 0 ? normal : all;
        }

        /// <summary>
        /// 유물 지급. 게임의 Event_Relic 과 같은 순서:
        /// 유물 추가 → 획득 기록 → 날아가는 연출 + 알림 → "확인" 선택지 하나만 남기고, 누르면 이벤트 종료
        /// </summary>
        private static void GiveRelic(Event_Start ev, EventElementUI ui, Action<List<EventSelect>> showSelections)
        {
            try
            {
                var playData = PlayData.Instance.Value;
                playData.Weights.TryGetValue(SeedType.Relic, out int weight);
                var relic = Candidates().GetRandom(playData.Seed, weight);
                playData.Weights[SeedType.Relic] = weight + 1;
                if (relic == null) { ev.Exit(ui); return; }

                playData.Relics.Add(relic);
                playData.recordSelect.Add(relic.Key);
                Plugin.Log.LogInfo("시작 선택지: 유물 획득 " + relic.Key);

                Vector2 relicPos = Getable.Get<PlayerRelicUI>().GetRelicPos(relic);
                string notice = Option.Instance.GetMessageText("Event_Relic_Descript1", relic.Key);
                FXManager.Instance.AddRelic(ui.symbol.transform.position, relicPos,
                    () => Getable.Get<NoticeUI>().Apply(relic.Sprite, notice));

                showSelections(new List<EventSelect>
                {
                    new EventSelect
                    {
                        Text = () => Option.Instance.GetMessageText(CharacterKey(ev) + "_End"),
                        Symbol = AssetManager.GetSprite("UI_Event_Icon_Cancel"),
                        Select = ev.Exit
                    }
                });
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
                ev.Exit(ui);
            }
        }
    }

    /// <summary>
    /// 협동 플레이 판 진행 여부 (CoopSession.IsRun).
    /// 게임 빌드 26896844 에서 추가된 클래스라, 구버전에서도 죽지 않도록 리플렉션으로 찾고 없으면 false.
    /// </summary>
    internal static class Coop
    {
        private static readonly Func<bool> getter = Create();

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
    }
}
