using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace PocketLegend.PeekHandInfo
{
    /// <summary>
    /// 전투 중 "꺼낼 아이템을 선택하세요" 같은 선택 창(SelectDeckUI)을 "접어두기" 했을 때,
    /// 손패 카드에 마우스를 올리면 평소처럼 카드가 올라오며 정보가 보이게 한다.
    ///
    /// 원인: 접어도 SelectDeckUI 의 투명한 전체 화면 영역이 마우스 입력을 가로채서
    ///       HandCardUI.OnPointerEnter → Pickup() 이 호출되지 않는다.
    /// 방법: 마우스 위치를 직접 검사해서 Pickup()/Put() 만 대신 호출한다.
    ///       클릭/드래그는 여전히 선택 창이 받으므로 카드를 "사용"할 수는 없다 (정보 보기 전용).
    /// </summary>
    [BepInPlugin(Guid, "Peek Hand Info", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "pocketlegend.peekhandinfo";
        // 모드 관리(Mod Switch)가 리플렉션으로 읽는 표시 정보: 분류(UX=조작 편의, UI=정보 표시, BAL=밸런스 변경) / 한국어 이름 / 협동 플레이 미적용
        public const string ModCategory = "UX";
        public const string NameKo = "손패 엿보기";
        public const bool SingleOnly = false;
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;

        private static readonly MethodInfo PickupMethod = AccessTools.Method(typeof(HandCardUI), "Pickup");
        private static readonly MethodInfo PutMethod = AccessTools.Method(typeof(HandCardUI), "Put");
        private static readonly MethodInfo IsPointerOnEmptyMethod = AccessTools.Method(typeof(SelectDeckUI), "IsPointerOnEmpty");
        private static readonly AccessTools.FieldRef<HandUI, ObservableList<HandCardUI>> CardsRef =
            AccessTools.FieldRefAccess<HandUI, ObservableList<HandCardUI>>("handCardUIs");

        private HandCardUI hovered;
        private readonly List<HandCardUI> candidates = new List<HandCardUI>();

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "모드 사용 여부 (타이틀 화면의 모드 관리에서도 변경 가능)");
        }

        private void Update()
        {
            HandCardUI target = null;
            try
            {
                if (IsPeekState(out var select, out var handUI))
                    target = FindCardUnderPointer(select, handUI);
            }
            catch (Exception e)
            {
                Log.LogError(e);
                enabled = false; // 오류가 반복되지 않도록 이번 실행 동안 중지
            }
            SetHovered(target);
        }

        /// <summary>전투 중 + 선택 창이 열려 있음 + 목록이 접혀 있음</summary>
        private static bool IsPeekState(out SelectDeckUI select, out HandUI handUI)
        {
            select = null;
            handUI = null;
            if (!Enabled.Value || MobileInput.IsMobile) return false;
            if (PlayData.Instance.Value?.partyData.Value?.IsBattle != true) return false;

            select = Getable.Get<SelectDeckUI>();
            if (select == null || !select.gameObject.activeInHierarchy) return false;
            if (select.deckUI == null || select.deckUI.gameObject.activeSelf) return false; // 펼쳐져 있으면 대상 아님

            var battle = Getable.Get<BattleUI>();
            handUI = battle != null ? battle.inventoryBattleUI?.handUI : null;
            return handUI != null && handUI.isActiveAndEnabled;
        }

        private HandCardUI FindCardUnderPointer(SelectDeckUI select, HandUI handUI)
        {
            // 선택 창의 보이는 요소(펼치기 버튼, 안내 문구 등) 위에서는 무시 — 게임의 기존 판정 재사용
            if (IsPointerOnEmptyMethod != null && !(bool)IsPointerOnEmptyMethod.Invoke(select, null)) return null;

            var cards = CardsRef(handUI);
            if (cards == null || cards.Count == 0) return null;

            Vector2 mouse = Input.mousePosition;
            var canvas = handUI.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;

            // 이미 올라와 있는 카드가 가장 위에 그려지므로 먼저 검사 (경계에서 깜빡임 방지)
            if (hovered != null && Contains(hovered, mouse, cam)) return hovered;

            // 나머지는 위에 그려진(형제 순서가 뒤인) 카드부터
            candidates.Clear();
            foreach (var c in cards) if (c != null && c.isActiveAndEnabled) candidates.Add(c);
            candidates.Sort((a, b) => b.transform.GetSiblingIndex().CompareTo(a.transform.GetSiblingIndex()));
            foreach (var c in candidates)
                if (Contains(c, mouse, cam)) return c;
            return null;
        }

        private static bool Contains(HandCardUI card, Vector2 mouse, Camera cam)
        {
            var rect = card.cardRectUI != null ? card.cardRectUI : (RectTransform)card.transform;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, mouse, cam);
        }

        private void SetHovered(HandCardUI target)
        {
            if (target == hovered) return;
            try
            {
                if (hovered != null) PutMethod.Invoke(hovered, null);
                hovered = target;
                if (hovered != null) PickupMethod.Invoke(hovered, null);
            }
            catch (Exception e)
            {
                Log.LogError(e);
                hovered = null;
            }
        }
    }
}
