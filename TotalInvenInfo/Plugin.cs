using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PocketLegend.TotalInvenInfo
{
    /// <summary>
    /// 전투 중 인벤토리의 현재 배치 기준으로 합계를 표로 보여준다.
    ///   | 총   피해 : x | 총   방어 : y |
    ///   |--------------|--------------|
    ///   | 최대 피해 : x | 최대 방어 : y |   (모드 관리에서 끄기 가능)
    ///   | 캐릭터 전용 줄                |   (모드 관리에서 끄기 가능)
    ///      고고: 총 마력 : 6   | 사용 가능 마력 : 5, 1
    ///      스택: 총 일월경 : 18 | 보유 일월경 : 8, 3, 2, 2, 1, 1, 1  /  총 빈칸 : k
    ///
    /// 계산 기준 (게임 코드 그대로 사용):
    ///  - 값: ItemParam.GetValue(isOrigin: false) = 아이템 설명에 보이는 "현재" 수치 (배치/강화/버프 반영)
    ///  - 무기 = 피해, 방어구 = 방어. 지금 사용 가능한 아이템만(InvenBehavior.IsInteraction, 이미 써서 비활성화된 것/잠긴 것 제외)
    ///    총합은 "수치 x 이번 턴 남은 사용 횟수(재사용 포함)"의 합, 최대는 아이템 1개 수치 중 최댓값
    ///  - 마석(Binding_Gem): 사용 가능한(IsInteraction 이 참인) 마석만. 총 마력 = 그 마력의 합 (사용해서 비활성화된 마석은 제외)
    ///    (게임의 Armor_WandAllGem "사용 가능한 모든 마석"과 같은 기준)
    ///  - 일월경(Binding_SunMoon): 인벤토리에 있기만 하면 버프를 주므로 비활성화 여부, 태양/달 상태와 관계없이 모두 더함, 목록은 큰 수부터
    ///  - 총 빈칸: 인벤토리 칸 중 비어 있거나 '환상'(EmptyType) 속성 아이템이 있는 칸
    ///  - 숫자 목록은 글자 크기를 바꾸지 않고, 칸을 넘치면 들어가는 데까지만 보이고 "..." 으로 줄임
    /// </summary>
    [BepInPlugin(Guid, "Total InvenInfo", "0.2.2")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "pocketlegend.totalinveninfo";
        // 모드 관리(Mod Switch)가 리플렉션으로 읽는 표시 정보
        public const string ModCategory = "UI";
        public const string NameKo = "인벤토리 합계";
        public const bool SingleOnly = false;
        public const string MenuOptions = "Display/Position;Display/ShowMaxRow;Display/ShowSpecialRow";

        public const string PosInventoryBottom = "InventoryBottom";
        public const string PosAboveHead = "AboveHead";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Position;
        internal static ConfigEntry<bool> ShowMaxRow;
        internal static ConfigEntry<bool> ShowSpecialRow;
        internal static ConfigEntry<Vector2> ExtraOffset;
        internal static ConfigEntry<float> FontSize;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "모드 사용 여부 (타이틀 화면의 모드 관리에서도 변경 가능)");
            Position = Config.Bind("Display", "Position", PosInventoryBottom, new ConfigDescription(
                "표시 위치: InventoryBottom = 인벤토리 아래 / AboveHead = 캐릭터 머리 위 (다른 모드와 UI 가 겹치면 바꿔 보세요)",
                new AcceptableValueList<string>(PosInventoryBottom, PosAboveHead)));
            ShowMaxRow = Config.Bind("Display", "ShowMaxRow", true, "최대 피해 / 최대 방어 줄 표시");
            ShowSpecialRow = Config.Bind("Display", "ShowSpecialRow", true, "캐릭터 전용 줄 표시 (고고: 마력, 스택: 일월경/빈칸)");
            ExtraOffset = Config.Bind("UI", "ExtraOffset", Vector2.zero, "표 추가 이동값 (px, 위치가 어색할 때 조정)");
            FontSize = Config.Bind("UI", "FontSize", 20f, new ConfigDescription("글자 크기", new AcceptableValueRange<float>(12f, 36f)));
        }

        /// <summary>모드 관리의 세부 옵션 줄에 보일 문구 (value 가 null 이면 항목 이름)</summary>
        public static string MenuText(string key, string value, bool korean)
        {
            switch (key)
            {
                case "Position":
                    if (value == null) return korean ? "표시 위치" : "Position";
                    if (value == PosAboveHead) return korean ? "캐릭터 머리 위" : "Above head";
                    return korean ? "인벤토리 아래" : "Below inventory";
                case "ShowMaxRow":
                    return value == null ? (korean ? "최대 피해/방어 줄" : "Max damage/defense row") : null;
                case "ShowSpecialRow":
                    return value == null ? (korean ? "캐릭터 전용 줄" : "Character stat row") : null;
                default:
                    return null;
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

        // InventoryBattleUI 는 플러그인 로드 전에 만들어져 있을 수 있으므로 패치 대신 찾아서 붙임
        private InventoryBattleUI attached;
        private void Update()
        {
            var battle = Getable.Get<InventoryBattleUI>();
            if (battle == null || battle == attached) return;
            attached = battle;
            try { InfoPanel.Create(battle); }
            catch (Exception e) { Log.LogError(e); }
        }
    }

    /// <summary>표에 들어갈 숫자 모음</summary>
    internal class Stats
    {
        public int DamageTotal, DamageMax, DefenseTotal, DefenseMax;
        public readonly List<int> MagicUsable = new List<int>();   // 고고: 지금 사용 가능한 마석 마력 (총 마력 = 이 목록의 합)
        public readonly List<int> SunMoon = new List<int>();       // 스택: 인벤토리의 일월경 수치 (태양/달 구분 없음)
        public int Empty;

        public static Stats Compute(Inventory inventory)
        {
            var s = new Stats();
            foreach (var item in inventory.list.ToList())
            {
                try
                {
                    var behavior = item.InvenBehavior;
                    if (behavior == null) continue;

                    // 무기/방어구: 지금 사용 가능한 것만 (이번 턴에 이미 써서 비활성화된 것, 잠긴 것은 제외)
                    // IsInteraction 은 게임이 "사용 가능한 아이템"을 판단할 때 쓰는 함수
                    if ((behavior.ItemType == ItemType.Weapon || behavior.ItemType == ItemType.Armor)
                        && behavior.IsUsable && behavior.IsInteraction(item))
                    {
                        int value = item.ItemParam.GetValue(isOrigin: false);
                        int uses = Math.Max(1, item.ItemParam.GetUseCount(isOrigin: false) ?? 1); // 이번 턴 남은 사용 횟수 (재사용 포함)
                        if (behavior.ItemType == ItemType.Weapon)
                        {
                            s.DamageTotal += value * uses;
                            s.DamageMax = Math.Max(s.DamageMax, value);
                        }
                        else
                        {
                            s.DefenseTotal += value * uses;
                            s.DefenseMax = Math.Max(s.DefenseMax, value);
                        }
                    }

                    // 고고: 마석. 사용하면 마석도 같이 비활성화되므로 사용 가능한 마석만 셈
                    //       ("사용 가능"은 게임의 Armor_WandAllGem "사용 가능한 모든 마석"과 같은 기준)
                    if (behavior is Binding_Gem && behavior.IsInteraction(item))
                        s.MagicUsable.Add(item.ItemParam.GetValue(isOrigin: false));

                    // 스택: 일월경 수치 (태양/달 상태와 관계없이). 인벤토리에 있기만 하면 버프를 주므로 비활성화 여부와 관계없이 모두 셈
                    if (behavior is Binding_SunMoon)
                        s.SunMoon.Add(item.ItemParam.GetValue(isOrigin: false));
                }
                catch { /* 계산 중 예외가 나는 특이 아이템은 건너뜀 */ }
            }
            // 목록은 큰 수부터
            s.MagicUsable.Sort((a, b) => b.CompareTo(a));
            s.SunMoon.Sort((a, b) => b.CompareTo(a));

            // 빈칸: 비어 있거나 '환상'(빈 칸으로 취급) 아이템이 있는 칸
            foreach (var pos in inventory.element.ToList())
            {
                var item = inventory.Get(pos);
                if (item == null || item.attributes.Contains(AttributeType.EmptyType)) s.Empty++;
            }
            return s;
        }
    }

    internal class InfoPanel : MonoBehaviour
    {
        private const int MaxRows = 4;
        private const float Pad = 8f, Gap = 10f;
        private static readonly Color BorderColor = new Color(0.85f, 0.75f, 0.55f, 0.9f);
        private static readonly Color BgColor = new Color(0.08f, 0.06f, 0.05f, 0.85f);
        private static readonly Color LineColor = new Color(0.85f, 0.75f, 0.55f, 0.5f);
        private const string ValueColor = "#FFD27F";

        private InventoryBattleUI battle;
        private CanvasGroup group;
        private Image background;
        private Image verticalLine, horizontalLine;
        private readonly TextMeshProUGUI[,] cells = new TextMeshProUGUI[MaxRows, 2];
        private float timer;
        private string lastKey;

        public static void Create(InventoryBattleUI battle)
        {
            var inventoryRect = (RectTransform)battle.inventoryUI.transform;
            var parent = (RectTransform)inventoryRect.parent;
            var fontSource = battle.GetComponentInChildren<TMP_Text>(true) ?? battle.battleUI?.GetComponentInChildren<TMP_Text>(true);

            var root = new GameObject("TotalInvenInfo", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(CanvasGroup));
            var rt = (RectTransform)root.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            // 인벤토리 바로 다음 형제로 두면 인벤토리 위에 그려지고, 인벤토리 크기 조절(다른 모드)의 영향은 받지 않음
            rt.SetSiblingIndex(inventoryRect.GetSiblingIndex() + 1);

            var panel = root.AddComponent<InfoPanel>();
            panel.battle = battle;
            panel.group = root.GetComponent<CanvasGroup>();
            panel.group.blocksRaycasts = false; // 아래 인벤토리/카드 조작을 막지 않도록
            panel.group.interactable = false;
            panel.background = root.GetComponent<Image>();
            panel.background.color = BgColor;
            panel.background.raycastTarget = false;
            var outline = root.GetComponent<Outline>();
            outline.effectColor = BorderColor;
            outline.effectDistance = new Vector2(2f, -2f);

            panel.verticalLine = CreateLine(rt, "VLine");
            panel.horizontalLine = CreateLine(rt, "HLine");

            for (int r = 0; r < MaxRows; r++)
                for (int c = 0; c < 2; c++)
                    panel.cells[r, c] = CreateText(rt, $"Cell_{r}_{c}", fontSource);

            Plugin.Log.LogInfo("인벤토리 합계 표 생성");
        }

        private static Image CreateLine(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = LineColor;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI CreateText(RectTransform parent, string name, TMP_Text fontSource)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            if (fontSource != null && fontSource.font != null)
            {
                tmp.font = fontSource.font;
                tmp.fontSharedMaterial = fontSource.font.material;
            }
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            tmp.richText = true;
            return tmp;
        }

        private void LateUpdate()
        {
            var party = PlayData.Instance?.Value?.partyData?.Value;
            var inventory = party?.inventory;
            bool visible = Plugin.Enabled.Value && battle != null && battle.isActiveAndEnabled
                           && battle.inventoryUI != null && battle.inventoryUI.gameObject.activeInHierarchy && inventory != null;
            group.alpha = visible ? 1f : 0f;
            if (!visible) return;

            timer -= Time.unscaledDeltaTime;
            if (timer <= 0f)
            {
                timer = 0.2f; // 0.2초마다 다시 계산 (인벤토리는 최대 49칸이라 부담 없음)
                Refresh(Stats.Compute(inventory));
            }
            Reposition();
        }

        /// <summary>
        /// 표의 칸 하나. Text 가 고정 문구이고, Values 가 있으면 "이름 : 8, 3, 2 ..." 처럼 숫자 목록을 칸 폭에 맞춰 자름
        /// </summary>
        private class CellSpec
        {
            public string Text = "";
            public string ListLabel;
            public List<int> Values;
            public string Key => Values == null ? Text : ListLabel + ":" + string.Join(",", Values);
        }

        /// <summary>
        /// 칸 하나: "총<pos>피해<pos>: 12" 처럼 &lt;pos&gt; 태그로 글자 시작 위치를 고정해서
        /// 줄마다 "피해 / 방어" 같은 단어와 ":" 가 세로로 나란히 보이게 함
        /// </summary>
        private static CellSpec Cell(string prefix, string word, string value)
        {
            bool ko = Plugin.IsKorean;
            string p1 = ko ? "2.3em" : "3.1em";
            string p2 = ko ? "5.6em" : "7.9em";   // 세 글자 단어(일월경, 빈칸 등)까지 들어가는 위치
            return new CellSpec { Text = $"{prefix}<pos={p1}>{word}<pos={p2}>: <color={ValueColor}>{value}</color>" };
        }

        /// <summary>숫자 목록 칸 (큰 수부터). 칸을 넘치면 들어가는 데까지만 보이고 "..." 으로 줄임</summary>
        private static CellSpec ListCell(string label, List<int> values) =>
            new CellSpec { ListLabel = label, Values = values };

        private static string ListText(string label, List<int> values, int count)
        {
            string shown = string.Join(", ", values.Take(count));
            if (count < values.Count) shown += count > 0 ? ", ..." : "...";
            if (values.Count == 0) shown = "-";
            return $"{label} : <color={ValueColor}>{shown}</color>";
        }

        private void Refresh(Stats s)
        {
            bool ko = Plugin.IsKorean;
            var rows = new List<(CellSpec left, CellSpec right)>
            {
                (Cell(ko ? "총" : "Total", ko ? "피해" : "Damage", s.DamageTotal.ToString()),
                 Cell(ko ? "총" : "Total", ko ? "방어" : "Defense", s.DefenseTotal.ToString()))
            };
            if (Plugin.ShowMaxRow.Value)
                rows.Add((Cell(ko ? "최대" : "Max", ko ? "피해" : "Damage", s.DamageMax.ToString()),
                          Cell(ko ? "최대" : "Max", ko ? "방어" : "Defense", s.DefenseMax.ToString())));
            if (Plugin.ShowSpecialRow.Value)
            {
                switch (PlayData.Instance.Value.characterType)
                {
                    case CharacterType.Gogo:
                        // | 총 마력 : 6 | 사용 가능 마력 : 5, 1 |
                        rows.Add((Cell(ko ? "총" : "Total", ko ? "마력" : "Mana", s.MagicUsable.Sum().ToString()),
                                  ListCell(ko ? "사용 가능 마력" : "Usable mana", s.MagicUsable)));
                        break;
                    case CharacterType.Stack:
                        // | 총 일월경 : 18 | 보유 일월경 : 8, 3, 2, 2, 1, 1, 1 |
                        rows.Add((Cell(ko ? "총" : "Total", ko ? "일월경" : "Mirror", s.SunMoon.Sum().ToString()),
                                  ListCell(ko ? "보유 일월경" : "Mirrors", s.SunMoon)));
                        rows.Add((Cell(ko ? "총" : "Total", ko ? "빈칸" : "Empty", s.Empty.ToString()), new CellSpec()));
                        break;
                }
            }

            // 내용이 같으면 다시 그리지 않음
            string key = string.Join("|", rows.Select(r => r.left.Key + "/" + r.right.Key)) + Plugin.FontSize.Value;
            if (key == lastKey) return;
            lastKey = key;
            Layout(rows);
        }

        private void Layout(List<(CellSpec left, CellSpec right)> rows)
        {
            float font = Plugin.FontSize.Value;
            float rowH = font * 1.5f;
            float colW = font * (Plugin.IsKorean ? 9.2f : 11.5f); // "<pos> 위치 + 숫자 3~4자리" 가 들어가는 폭
            float width = colW * 2 + Pad * 4;
            float height = rowH * rows.Count + Pad * 2;
            var rt = (RectTransform)transform;
            rt.sizeDelta = new Vector2(width, height);

            for (int r = 0; r < MaxRows; r++)
            {
                for (int c = 0; c < 2; c++)
                {
                    var tmp = cells[r, c];
                    bool used = r < rows.Count;
                    tmp.gameObject.SetActive(used);
                    if (!used) continue;
                    // 글자 크기는 고정 (자동 축소 안 함), 칸 폭을 넘는 글자는 잘라냄
                    tmp.enableAutoSizing = false;
                    tmp.fontSize = font;
                    tmp.overflowMode = TextOverflowModes.Ellipsis;
                    var trt = tmp.rectTransform;
                    trt.sizeDelta = new Vector2(colW, rowH);
                    trt.anchoredPosition = new Vector2(Pad + c * (colW + Pad * 2), -Pad - rowH * r);
                    tmp.text = Fit(tmp, c == 0 ? rows[r].left : rows[r].right, colW);
                }
            }

            // 가운데 세로줄, 첫 줄(총 피해/총 방어) 아래 가로줄
            var v = verticalLine.rectTransform;
            v.anchorMin = v.anchorMax = new Vector2(0.5f, 0.5f);
            v.sizeDelta = new Vector2(2f, height - Pad);
            v.anchoredPosition = Vector2.zero;
            var h = horizontalLine.rectTransform;
            h.gameObject.SetActive(rows.Count > 1);
            h.anchorMin = h.anchorMax = new Vector2(0.5f, 1f);
            h.sizeDelta = new Vector2(width - Pad * 2, 2f);
            h.anchoredPosition = new Vector2(0f, -Pad - rowH);
        }

        /// <summary>숫자 목록은 칸 폭에 들어가는 개수까지만 넣고 나머지는 "..." (숫자 중간에서 잘리지 않게 통째로 뺌)</summary>
        private static string Fit(TextMeshProUGUI tmp, CellSpec cell, float width)
        {
            if (cell.Values == null) return cell.Text;
            for (int count = cell.Values.Count; count > 0; count--)
            {
                string text = ListText(cell.ListLabel, cell.Values, count);
                if (tmp.GetPreferredValues(text).x <= width) return text;
            }
            return ListText(cell.ListLabel, cell.Values, 0);
        }

        private void Reposition()
        {
            var rt = (RectTransform)transform;
            var parent = (RectTransform)rt.parent;
            Vector2 local;
            if (Plugin.Position.Value == Plugin.PosAboveHead && TryHeadPoint(parent, out local))
            {
                rt.pivot = new Vector2(0.5f, 0f);
                local += new Vector2(0f, Gap);
            }
            else if (TryInventoryBottom(parent, out local))
            {
                rt.pivot = new Vector2(0.5f, 1f);
                local += new Vector2(0f, -Gap);
            }
            else return;

            local += Plugin.ExtraOffset.Value;
            // anchor 가 부모 중앙이므로 부모 기준 좌표에서 중앙을 빼서 anchoredPosition 으로
            Vector2 pos = local - parent.rect.center;

            // 화면(부모) 밖으로 나가지 않게 안쪽으로 당김
            Vector2 size = rt.sizeDelta;
            Rect pr = parent.rect;
            float minX = pr.xMin + size.x * rt.pivot.x + 4f - pr.center.x;
            float maxX = pr.xMax - size.x * (1f - rt.pivot.x) - 4f - pr.center.x;
            float minY = pr.yMin + size.y * rt.pivot.y + 4f - pr.center.y;
            float maxY = pr.yMax - size.y * (1f - rt.pivot.y) - 4f - pr.center.y;
            pos.x = Mathf.Clamp(pos.x, minX, Mathf.Max(minX, maxX));
            pos.y = Mathf.Clamp(pos.y, minY, Mathf.Max(minY, maxY));
            rt.anchoredPosition = pos;
        }

        /// <summary>인벤토리 테두리(bezel) 칸들의 아래쪽 가운데 (부모 좌표)</summary>
        private bool TryInventoryBottom(RectTransform parent, out Vector2 local)
        {
            local = default;
            var inv = battle.inventoryUI;
            var source = inv.bezelParent != null && HasActiveChild(inv.bezelParent) ? inv.bezelParent : inv.elementParent;
            if (source == null) return false;

            bool any = false;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue;
            var corners = new Vector3[4];
            for (int i = 0; i < source.childCount; i++)
            {
                var child = source.GetChild(i) as RectTransform;
                if (child == null || !child.gameObject.activeSelf) continue;
                child.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    Vector2 p = parent.InverseTransformPoint(corner);
                    minX = Mathf.Min(minX, p.x);
                    maxX = Mathf.Max(maxX, p.x);
                    minY = Mathf.Min(minY, p.y);
                }
                any = true;
            }
            if (!any) return false;
            local = new Vector2((minX + maxX) * 0.5f, minY);
            return true;
        }

        private static bool HasActiveChild(Transform t)
        {
            for (int i = 0; i < t.childCount; i++)
                if (t.GetChild(i).gameObject.activeSelf) return true;
            return false;
        }

        private static readonly System.Reflection.PropertyInfo SkeletonProp =
            AccessTools.Property(typeof(CharacterUI), "SkeletonAnimation");

        /// <summary>플레이어 캐릭터 그림(Spine)의 머리 위 가운데 (부모 좌표)</summary>
        private static bool TryHeadPoint(RectTransform parent, out Vector2 local)
        {
            local = default;
            var characterUI = PlayData.Instance?.Value?.playerData?.CharacterUI;
            if (characterUI == null) return false;

            // 캐릭터는 UI 가 아니라 월드에 그려지므로: 그림의 경계 상자 → 화면 좌표 → 이 UI 의 좌표
            var skeleton = SkeletonProp?.GetValue(characterUI) as Component;
            var renderer = skeleton != null ? skeleton.GetComponent<Renderer>() : null;
            if (renderer == null) return false;
            var bounds = renderer.bounds;
            var worldCam = FindCamera(renderer.gameObject.layer);
            if (worldCam == null) return false;
            Vector3 screen = worldCam.WorldToScreenPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z));

            var canvas = parent.GetComponentInParent<Canvas>()?.rootCanvas;
            var uiCam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, uiCam, out local);
        }

        private static Camera FindCamera(int layer)
        {
            Camera best = null;
            foreach (var cam in Camera.allCameras)
            {
                if ((cam.cullingMask & (1 << layer)) == 0) continue;
                if (best == null || cam.depth < best.depth) best = cam;
            }
            return best ?? Camera.main;
        }
    }
}
