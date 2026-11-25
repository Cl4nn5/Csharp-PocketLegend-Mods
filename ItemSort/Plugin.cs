using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PocketLegend.ItemSort
{
    public enum SortMode
    {
        Acquired, // 획득순 (게임 기본)
        Type,     // 유형별
        Name,     // 이름순(가나다)
        Enhance,  // 강화순
    }

    /// <summary>
    /// 아이템 목록을 보여주는 모든 DeckUI(아이템 메뉴, "꺼낼 아이템을 선택하세요" 같은 선택 창,
    /// 전투 중 뽑기/버림/소멸 더미 등)에 정렬 버튼을 붙인다.
    /// </summary>
    [BepInPlugin(Guid, "Item Sort", "0.3.1")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "pocketlegend.itemsort";
        // 모드 관리(Mod Switch)가 리플렉션으로 읽는 표시 정보: 분류(UX=조작 편의, UI=정보 표시, BAL=밸런스 변경) / 한국어 이름 / 협동 플레이 미적용
        public const string ModCategory = "UX";
        public const string NameKo = "아이템 정렬";
        public const bool SingleOnly = false;

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<SortMode> CurrentSort;
        internal static ConfigEntry<float> Gap;
        internal static ConfigEntry<Vector2> ExtraOffset;
        internal static ConfigEntry<bool> DebugLog;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "모드 사용 여부 (타이틀 화면의 모드 관리에서도 변경 가능)");
            CurrentSort = Config.Bind("General", "SortMode", SortMode.Acquired, "마지막으로 선택한 정렬 방식 (모든 목록 공통)");
            Gap = Config.Bind("UI", "Gap", 8f, "정렬 버튼 줄과 아이템 목록 영역 사이 간격 (px)");
            ExtraOffset = Config.Bind("UI", "ExtraOffset", Vector2.zero, "정렬 버튼 줄 추가 이동값 (px, 위치가 어색할 때 조정)");
            DebugLog = Config.Bind("Debug", "LogHierarchy", false, "목록 화면마다 처음 열 때 UI 계층 구조를 로그에 출력 (위치 조정용)");

            new Harmony(Guid).PatchAll(typeof(Patches));
            Log.LogInfo("Item Sort loaded");
        }
    }

    internal static class Patches
    {
        // 목록이 (다시) 채워질 때: 버튼 줄 보장 + 정렬
        [HarmonyPostfix, HarmonyPatch(typeof(DeckUI), nameof(DeckUI.Apply))]
        private static void DeckUI_Apply(DeckUI __instance)
        {
            try
            {
                var bar = SortToolbar.Ensure(__instance);
                bar?.Refresh();
            }
            catch (Exception e) { Plugin.Log.LogError(e); }
        }

        // 아이템이 하나 추가될 때: 정렬 유지
        [HarmonyPostfix, HarmonyPatch(typeof(DeckUI), "Deck_OnAdd")]
        private static void DeckUI_OnAdd(DeckUI __instance)
        {
            if (Plugin.Enabled.Value)
                Sorter.Sort(__instance, Plugin.CurrentSort.Value);
        }

        // 아이템 메뉴는 열 때마다 강화 상태가 바뀌었을 수 있으므로 다시 정렬
        [HarmonyPostfix, HarmonyPatch(typeof(Getable), nameof(Getable.Show))]
        private static void Getable_Show(Getable __instance)
        {
            if (__instance is PlayerDeckUI menu && menu.deckUI != null)
                DeckUI_Apply(menu.deckUI);
        }
    }

    internal static class Sorter
    {
        private static readonly AccessTools.FieldRef<DeckUI, ObservableList<CardData>> DeckRef =
            AccessTools.FieldRefAccess<DeckUI, ObservableList<CardData>>("<Deck>k__BackingField");

        private static readonly Regex TagRegex = new Regex("<[^>]*>", RegexOptions.Compiled);

        public static void Sort(DeckUI deckUI, SortMode mode)
        {
            if (deckUI == null) return;
            var deck = DeckRef(deckUI);
            if (deck == null) return;

            // 획득순 = 원본 리스트 순서
            var acquired = deck.ToList();
            var index = new Dictionary<CardData, int>();
            for (int i = 0; i < acquired.Count; i++) index[acquired[i]] = i;

            IEnumerable<CardData> ordered;
            switch (mode)
            {
                case SortMode.Type:
                    ordered = acquired
                        .OrderBy(TypeOrder)
                        .ThenBy(SafeName, StringComparer.Ordinal)
                        .ThenBy(c => index[c]);
                    break;
                case SortMode.Name:
                    // 한글 음절은 유니코드에서 가나다 순으로 배치되어 있어 Ordinal 비교로 충분
                    ordered = acquired
                        .OrderBy(SafeName, StringComparer.Ordinal)
                        .ThenBy(c => index[c]);
                    break;
                case SortMode.Enhance:
                    ordered = acquired
                        .OrderByDescending(c => c.enhances.Count)
                        .ThenBy(c => index[c]);
                    break;
                default:
                    ordered = acquired;
                    break;
            }

            // SetAsLastSibling 을 순서대로 호출하면 그 순서로 배치됨 (GridLayoutGroup 기준)
            foreach (var card in ordered)
            {
                if (deckUI.CardUIs.TryGetValue(card, out var ui) && ui != null)
                    ui.transform.SetAsLastSibling();
            }
        }

        private static int TypeOrder(CardData c)
        {
            try { return (int)c.ItemBehavior.ItemType; } catch { return 99; }
        }

        private static string SafeName(CardData c)
        {
            try { return TagRegex.Replace(c.ItemBehavior.Name ?? "", "").Trim(); }
            catch { return c.ItemBehaviorKey ?? ""; }
        }
    }

    /// <summary>DeckUI 하나에 붙는 정렬 버튼 줄</summary>
    internal class SortToolbar : MonoBehaviour
    {
        private const string ToolbarName = "ItemSortToolbar";
        private const float ButtonWidth = 130f, ButtonHeight = 48f, Spacing = 8f;

        private static readonly (SortMode mode, string label)[] Buttons =
        {
            (SortMode.Acquired, "획득순"),
            (SortMode.Type, "유형별"),
            (SortMode.Name, "이름순"),
            (SortMode.Enhance, "강화순"),
        };

        private static readonly Color Normal = new Color(0.12f, 0.10f, 0.09f, 0.85f);
        private static readonly Color Selected = new Color(0.78f, 0.55f, 0.20f, 0.95f);
        private static readonly HashSet<string> Dumped = new HashSet<string>();

        private DeckUI deckUI;
        private readonly List<(SortMode mode, Image bg)> images = new List<(SortMode, Image)>();

        public static SortToolbar Ensure(DeckUI deckUI)
        {
            var parent = deckUI.transform as RectTransform;
            if (parent == null) return null;
            if (deckUI.cardParent == parent) return null; // 카드와 같은 부모면 정렬 시 섞이므로 생략

            var existing = parent.Find(ToolbarName);
            if (existing != null) return existing.GetComponent<SortToolbar>();
            if (!Plugin.Enabled.Value) return null; // 꺼져 있으면 새로 만들지 않음

            var owner = deckUI.GetComponentInParent<Getable>(true);
            string ownerName = owner != null ? owner.GetType().Name : deckUI.transform.parent?.name;
            if (Plugin.DebugLog.Value && Dumped.Add(ownerName + "/" + deckUI.name))
                DumpHierarchy(deckUI.transform, 0);

            var bar = new GameObject(ToolbarName, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var barRt = (RectTransform)bar.transform;
            barRt.SetParent(parent, false);
            barRt.sizeDelta = new Vector2(Buttons.Length * ButtonWidth + (Buttons.Length - 1) * Spacing, ButtonHeight);

            var layout = bar.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = Spacing;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            layout.childControlWidth = layout.childControlHeight = true;

            var toolbar = bar.AddComponent<SortToolbar>();
            toolbar.deckUI = deckUI;
            toolbar.Build(deckUI.GetComponentInParent<Canvas>(true)?.GetComponentInChildren<TMP_Text>(true));
            bar.transform.SetAsLastSibling();

            Plugin.Log.LogInfo($"정렬 버튼 추가: {ownerName}/{deckUI.name}");
            return toolbar;
        }

        private void Build(TMP_Text fontSrc)
        {
            foreach (var (mode, label) in Buttons)
            {
                var go = new GameObject("Sort_" + mode, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(transform, false);
                var img = go.GetComponent<Image>();
                var captured = mode;
                go.GetComponent<Button>().onClick.AddListener(() =>
                {
                    Plugin.CurrentSort.Value = captured;
                    Plugin.Log.LogInfo($"정렬 변경: {captured}");
                    Refresh();
                });
                images.Add((mode, img));

                var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                var textRt = (RectTransform)textGo.transform;
                textRt.SetParent(go.transform, false);
                textRt.anchorMin = Vector2.zero;
                textRt.anchorMax = Vector2.one;
                textRt.offsetMin = textRt.offsetMax = Vector2.zero;
                var tmp = textGo.GetComponent<TextMeshProUGUI>();
                if (fontSrc != null)
                {
                    tmp.font = fontSrc.font;
                    tmp.fontSharedMaterial = fontSrc.font != null ? fontSrc.font.material : fontSrc.fontSharedMaterial;
                }
                tmp.text = label;
                tmp.fontSize = 24f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.white;
                tmp.raycastTarget = false;
            }
        }

        // 목록 화면이 켜질 때마다(전투 더미 열기, 선택 창 열기 등) 위치/정렬/강조 갱신
        private void OnEnable() => Refresh();

        public void Refresh()
        {
            if (deckUI == null) return;
            // OnEnable 중 SetActive 호출은 Unity 오류가 날 수 있어 CanvasGroup 으로 숨김 처리
            var group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>(); // Unity 객체는 ?? 대신 == null 비교
            bool on = Plugin.Enabled.Value;
            group.alpha = on ? 1f : 0f;
            group.interactable = group.blocksRaycasts = on;
            if (!on)
            {
                Sorter.Sort(deckUI, SortMode.Acquired);
                return;
            }
            Reposition();
            Sorter.Sort(deckUI, Plugin.CurrentSort.Value);
            foreach (var (mode, img) in images)
                if (img != null) img.color = mode == Plugin.CurrentSort.Value ? Selected : Normal;
        }

        /// <summary>목록 영역(ScrollRect 의 Viewport) 바로 위 가운데에 배치</summary>
        private void Reposition()
        {
            var rt = (RectTransform)transform;
            var parent = (RectTransform)rt.parent;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f);

            var scroll = deckUI.GetComponentInChildren<ScrollRect>(true);
            var target = scroll != null ? (scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform) : null;
            Vector2 pos;
            if (target != null)
            {
                var corners = new Vector3[4];
                target.GetWorldCorners(corners); // 1: 왼쪽 위, 2: 오른쪽 위
                Vector2 local = parent.InverseTransformPoint((corners[1] + corners[2]) * 0.5f);
                pos = local - parent.rect.center + new Vector2(0f, Plugin.Gap.Value);
            }
            else
            {
                // 스크롤 영역이 없으면 화면 위쪽 기준
                pos = new Vector2(0f, parent.rect.height * 0.5f - 95f - ButtonHeight);
            }
            rt.anchoredPosition = pos + Plugin.ExtraOffset.Value;
        }

        private static void DumpHierarchy(Transform t, int depth)
        {
            if (depth > 4) return;
            var rt = t as RectTransform;
            var comps = string.Join(",", t.GetComponents<Component>().Select(c => c.GetType().Name));
            Plugin.Log.LogInfo($"{new string(' ', depth * 2)}{t.name} [{comps}] {(rt != null ? rt.rect.ToString() : "")}");
            for (int i = 0; i < t.childCount && i < 15; i++)
            {
                var child = t.GetChild(i);
                if (child.GetComponent<CardUI>() != null) continue; // 카드는 생략
                DumpHierarchy(child, depth + 1);
            }
        }
    }
}
