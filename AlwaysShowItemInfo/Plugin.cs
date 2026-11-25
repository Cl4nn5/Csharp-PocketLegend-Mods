using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PocketLegend.AlwaysShowItemInfo
{
    /// <summary>
    /// 아이템 목록(DeckUI)을 쓰는 모든 화면에서 우클릭으로 아이템 상세보기(CardDetailUI)를 연다.
    /// 원래 게임은 "강화할 아이템을 선택하세요" 같은 선택 창(SelectDeckUI)에서 좌클릭만 처리하고 우클릭은 무시한다.
    /// (보상 카드 선택 창 SelectItemUI 는 원래부터 우클릭 상세보기가 있어서 같은 방식을 따름)
    /// </summary>
    [BepInPlugin(Guid, "Always Show Item Info", "0.2.1")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "pocketlegend.alwaysshowiteminfo";
        // 모드 관리(Mod Switch)가 리플렉션으로 읽는 표시 정보: 분류(UX=조작 편의, UI=정보 표시, BAL=밸런스 변경) / 한국어 이름 / 협동 플레이 미적용
        public const string ModCategory = "UX";
        public const string NameKo = "우클릭 상세보기";
        public const bool SingleOnly = false;
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "모드 사용 여부 (타이틀 화면의 모드 관리에서도 변경 가능)");
            new Harmony(Guid).PatchAll(typeof(Patches));
        }

        // 카드 툴팁(GuideLinkUI)이 떠 있을 때 우클릭 → 그 카드 상세보기
        // 예: 이벤트 선택지 문구 속 아이템 이름(음양도 등)에 마우스를 올렸을 때 뜨는 카드
        private void Update()
        {
            if (!Enabled.Value || !Input.GetMouseButtonDown(1)) return;
            var card = Patches.TooltipCard;
            var guide = GuideLinkUI.Instance;
            if (card == null || guide == null || guide.cardUI == null || !guide.cardUI.gameObject.activeInHierarchy) return;
            guide.Hide();
            Patches.OpenDetail(card);
        }
    }

    internal static class Patches
    {
        // 카드 UI 가 만들어질 때 클릭 처리를 감싸서 우클릭이면 상세보기를 연다
        [HarmonyPostfix, HarmonyPatch(typeof(DeckUI), "Deck_OnAdd")]
        private static void DeckUI_OnAdd(DeckUI __instance, CardData cardData)
        {
            if (!__instance.CardUIs.TryGetValue(cardData, out var cardUI) || cardUI == null) return;

            Action<PointerEventData> original = cardUI.OnClick;
            cardUI.OnClick = eventData =>
            {
                if (Plugin.Enabled.Value && eventData.button == PointerEventData.InputButton.Right)
                {
                    OpenDetail(cardData);
                    return;
                }
                original?.Invoke(eventData);
            };
        }

        // 공용 툴팁에 현재 표시 중인 카드 기억
        internal static ItemParam TooltipCard;

        [HarmonyPostfix, HarmonyPatch(typeof(GuideLinkUI), nameof(GuideLinkUI.Show))]
        private static void GuideLinkUI_Show(GuideLinkParam guideLinkParam, Component owner)
        {
            // 인벤토리 아이템(ItemUI)은 우클릭이 "사용"이라 상세보기와 겹치므로 제외
            TooltipCard = owner is ItemUI ? null : guideLinkParam?.card;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(GuideLinkUI), nameof(GuideLinkUI.Hide), new Type[0])]
        private static void GuideLinkUI_Hide() => TooltipCard = null;

        private static void OpenDetail(CardData cardData) => OpenDetail(cardData.ItemParam);

        internal static void OpenDetail(ItemParam itemParam)
        {
            try
            {
                var detail = Getable.Get<CardDetailUI>();
                if (detail == null || itemParam == null) return;
                detail.Open(itemParam);
                detail.transform.SetAsLastSibling(); // 선택 창보다 뒤에 깔리지 않도록
            }
            catch (Exception e) { Plugin.Log.LogError(e); }
        }
    }
}
