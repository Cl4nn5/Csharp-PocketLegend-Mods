using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PocketLegend.ShowCardShortcut
{
    /// <summary>
    /// 전투 화면에서
    ///  1) 손패 아이템 위에 숫자 단축키(1~9, 0)를 표시한다. (게임에 이미 있는 단축키: BattleUI.UpdateHandKeys)
    ///  2) 턴 종료 버튼 아래에 "손패 수 / 최대(PartyData.handMaxCount = 10)"를 표시한다.
    /// </summary>
    [BepInPlugin(Guid, "Show Card Shortcut", "0.2.1")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "pocketlegend.showcardshortcut";
        // 모드 관리(Mod Switch)가 리플렉션으로 읽는 표시 정보: 분류(UX=조작 편의, UI=정보 표시, BAL=밸런스 변경) / 한국어 이름 / 협동 플레이 미적용
        public const string ModCategory = "UI";
        public const string NameKo = "단축키 표시";
        public const bool SingleOnly = false;
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> ShowHandCount;
        internal static ConfigEntry<Vector2> HotkeyOffset;
        internal static ConfigEntry<Vector2> CountOffset;

        private BattleUI attached;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "모드 사용 여부 (타이틀 화면의 모드 관리에서도 변경 가능)");
            ShowHandCount = Config.Bind("General", "ShowHandCount", true, "턴 종료 버튼 위 손패 수 / 최대 표시");
            HotkeyOffset = Config.Bind("UI", "HotkeyOffset", new Vector2(0f, 6f), "단축키 표시 위치 (카드 위쪽 가운데 기준, px)");
            CountOffset = Config.Bind("UI", "CountOffset", new Vector2(0f, 6f), "손패 수 표시 위치 (턴 종료 버튼 위쪽 가운데 기준, px)");
        }

        // BattleUI 는 플러그인 로드 전에 만들어져 있을 수 있으므로 찾아서 붙임
        private void Update()
        {
            var battle = Getable.Get<BattleUI>();
            if (battle == null || battle == attached) return;
            attached = battle;
            try
            {
                HotkeyLabeler.Attach(battle);
                HandCountLabel.Attach(battle);
            }
            catch (Exception e) { Log.LogError(e); }
        }
    }

    internal static class Ui
    {
        public static TMP_FontAsset Font;

        public static void CaptureFont(Component from)
        {
            if (Font != null) return;
            var t = from.GetComponentInChildren<TMP_Text>(true);
            if (t != null) Font = t.font;
        }

        /// <summary>테두리 + 어두운 배경 + 글자로 된 작은 라벨</summary>
        public static (RectTransform root, TextMeshProUGUI text) Label(string name, Transform parent, Vector2 size, float fontSize)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Outline));
            var rt = (RectTransform)root.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = size;
            var img = root.GetComponent<Image>();
            img.color = new Color(0.08f, 0.06f, 0.05f, 0.85f);
            img.raycastTarget = false;
            var outline = root.GetComponent<Outline>();
            outline.effectColor = new Color(0.85f, 0.75f, 0.55f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var trt = (RectTransform)textGo.transform;
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            if (Font != null)
            {
                tmp.font = Font;
                tmp.fontSharedMaterial = Font.material;
            }
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            return (rt, tmp);
        }
    }

    /// <summary>손패 카드마다 단축키 숫자를 붙이고 순서가 바뀌면 갱신</summary>
    internal class HotkeyLabeler : MonoBehaviour
    {
        private const string LabelName = "HandHotkeyLabel";
        private static readonly AccessTools.FieldRef<HandUI, ObservableList<HandCardUI>> CardsRef =
            AccessTools.FieldRefAccess<HandUI, ObservableList<HandCardUI>>("handCardUIs");

        private HandUI handUI;

        public static void Attach(BattleUI battle)
        {
            var handUI = battle.inventoryBattleUI?.handUI;
            if (handUI == null) { Plugin.Log.LogWarning("HandUI 를 찾지 못함"); return; }
            Ui.CaptureFont(battle);
            if (handUI.GetComponent<HotkeyLabeler>() == null)
                handUI.gameObject.AddComponent<HotkeyLabeler>().handUI = handUI;
            Plugin.Log.LogInfo("손패 단축키 표시 연결");
        }

        private void LateUpdate()
        {
            var cards = CardsRef(handUI);
            if (cards == null) return;
            bool on = Plugin.Enabled.Value;
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (card == null) continue;
                var host = card.cardRectUI != null ? card.cardRectUI : (RectTransform)card.transform;
                var label = host.Find(LabelName);
                if (label == null)
                {
                    if (!on) continue;
                    var (rt, _) = Ui.Label(LabelName, host, new Vector2(34f, 30f), 22f);
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                    rt.pivot = new Vector2(0.5f, 0f);
                    label = rt;
                }
                // 게임의 단축키 순서: 1,2,...,9,0 (11번째 이후는 단축키 없음)
                bool hasKey = on && i < 10;
                if (label.gameObject.activeSelf != hasKey) label.gameObject.SetActive(hasKey);
                if (!hasKey) continue;
                ((RectTransform)label).anchoredPosition = Plugin.HotkeyOffset.Value;
                var text = label.GetComponentInChildren<TextMeshProUGUI>();
                string key = i == 9 ? "0" : (i + 1).ToString();
                if (text.text != key) text.text = key;
            }
        }
    }

    /// <summary>턴 종료 버튼 위 "손패 수 / 최대"</summary>
    internal class HandCountLabel : MonoBehaviour
    {
        private RectTransform button;
        private TextMeshProUGUI text;
        private CanvasGroup group;
        private int lastCount = -1;

        public static void Attach(BattleUI battle)
        {
            if (battle.turnEndButtonUI == null) { Plugin.Log.LogWarning("턴 종료 버튼을 찾지 못함"); return; }
            Ui.CaptureFont(battle);
            var button = (RectTransform)battle.turnEndButtonUI.transform;
            // 버튼 자식이면 버튼 애니메이션에 휘말리고, 버튼 부모에 LayoutGroup 이 있으면 배치가 밀릴 수 있어
            // BattleUI 루트에 두고 위치만 버튼 기준으로 계산
            var (rt, tmp) = Ui.Label("HandCountLabel", battle.transform, new Vector2(120f, 34f), 22f);
            var comp = rt.gameObject.AddComponent<HandCountLabel>();
            comp.button = button;
            comp.text = tmp;
            comp.group = rt.gameObject.AddComponent<CanvasGroup>();
            comp.group.blocksRaycasts = false;
            rt.SetAsLastSibling();
            Plugin.Log.LogInfo("손패 수 표시 연결");
        }

        private void LateUpdate()
        {
            var party = PlayData.Instance.Value?.partyData.Value;
            bool visible = Plugin.Enabled.Value && Plugin.ShowHandCount.Value && party != null && party.hand != null
                           && button != null && button.gameObject.activeInHierarchy;
            group.alpha = visible ? 1f : 0f;
            if (!visible) return;

            Reposition();
            int count = party.hand.Count;
            if (count == lastCount) return;
            lastCount = count;
            int max = PartyData.handMaxCount;
            string countText = count >= max ? $"<color=#FF6B6B>{count}</color>" : count.ToString();
            text.text = $"{countText} / {max}";
        }

        private void Reposition()
        {
            var rt = (RectTransform)transform;
            var parent = (RectTransform)rt.parent;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            // 턴 종료 버튼이 화면 맨 아래에 붙어 있어 버튼 "위"에 배치
            rt.pivot = new Vector2(0.5f, 0f);
            var corners = new Vector3[4];
            button.GetWorldCorners(corners); // 1: 왼쪽 위, 2: 오른쪽 위
            Vector2 local = parent.InverseTransformPoint((corners[1] + corners[2]) * 0.5f);
            rt.anchoredPosition = local - parent.rect.center + Plugin.CountOffset.Value;
        }
    }
}
