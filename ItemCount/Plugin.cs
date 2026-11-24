using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PocketLegend.ItemCount
{
    /// <summary>
    /// 오른쪽 위 "아이템" 아이콘 아래에 보유 아이템 수를 유형별로 2x2 박스로 표시한다.
    ///   | 무기 n | 방어구 n |
    ///   | 스킬 n | 귀속 n   |
    /// </summary>
    [BepInPlugin(Guid, "Item Type Count", "0.1.1")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "pocketlegend.itemcount";
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<Vector2> ExtraOffset;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "모드 사용 여부 (타이틀 화면의 모드 관리에서도 변경 가능)");
            ExtraOffset = Config.Bind("UI", "ExtraOffset", Vector2.zero, "박스 추가 이동값 (px, 위치가 어색할 때 조정)");
        }

        // ResourceUI 는 플러그인 로드 전에 이미 만들어져 있을 수 있으므로 Awake 패치 대신 찾아서 붙임
        private ResourceUI attached;
        private void Update()
        {
            var resource = Getable.Get<ResourceUI>();
            if (resource == null || resource == attached) return;
            attached = resource;
            try { CountBox.Create(resource); }
            catch (Exception e) { Log.LogError(e); }
        }
    }

    internal class CountBox : MonoBehaviour
    {
        private static readonly (ItemType type, string label)[] Cells =
        {
            (ItemType.Weapon, "무기"),
            (ItemType.Armor, "방어구"),
            (ItemType.Skill, "스킬"),
            (ItemType.Binding, "귀속"),
        };

        private const float CellW = 104f, CellH = 34f, Border = 2f, Gap = 6f;
        private static readonly Color BorderColor = new Color(0.85f, 0.75f, 0.55f, 0.9f);
        private static readonly Color BgColor = new Color(0.08f, 0.06f, 0.05f, 0.8f);

        private ResourceUI resource;
        private RectTransform anchorTarget;
        private CanvasGroup group;
        private readonly TextMeshProUGUI[] texts = new TextMeshProUGUI[4];
        private readonly int[] lastCounts = { -1, -1, -1, -1 };
        private float timer;

        public static void Create(ResourceUI resource)
        {
            if (resource.deckUI == null) return;
            var parent = (RectTransform)resource.transform;
            var font = resource.GetComponentInChildren<TMP_Text>(true);

            // 2x2 격자: 바깥 테두리 색 배경 위에 칸마다 안쪽 배경을 깔아서 칸 구분선이 생기게 함
            var root = new GameObject("ItemCountBox", typeof(RectTransform), typeof(Image), typeof(GridLayoutGroup), typeof(CanvasGroup));
            var rt = (RectTransform)root.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(CellW * 2 + Border * 3, CellH * 2 + Border * 3);
            var rootImg = root.GetComponent<Image>();
            rootImg.color = BorderColor;
            rootImg.raycastTarget = false;
            var grid = root.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(CellW, CellH);
            grid.spacing = new Vector2(Border, Border);
            grid.padding = new RectOffset((int)Border, (int)Border, (int)Border, (int)Border);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;

            var box = root.AddComponent<CountBox>();
            box.resource = resource;
            box.anchorTarget = (RectTransform)resource.deckUI.transform;
            box.group = root.GetComponent<CanvasGroup>();
            box.group.blocksRaycasts = false; // 아래 버튼 클릭을 막지 않도록

            for (int i = 0; i < Cells.Length; i++)
            {
                var cell = new GameObject("Cell_" + Cells[i].type, typeof(RectTransform), typeof(Image));
                cell.transform.SetParent(rt, false);
                var img = cell.GetComponent<Image>();
                img.color = BgColor;
                img.raycastTarget = false;

                var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
                var trt = (RectTransform)textGo.transform;
                trt.SetParent(cell.transform, false);
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(6f, 0f);
                trt.offsetMax = new Vector2(-6f, 0f);
                var tmp = textGo.GetComponent<TextMeshProUGUI>();
                if (font != null)
                {
                    tmp.font = font.font;
                    tmp.fontSharedMaterial = font.font != null ? font.font.material : font.fontSharedMaterial;
                }
                tmp.fontSize = 20f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.white;
                tmp.raycastTarget = false;
                tmp.enableWordWrapping = false;
                box.texts[i] = tmp;
            }

            root.transform.SetAsLastSibling();
            Plugin.Log.LogInfo("유형별 아이템 수 박스 생성");
        }

        private void LateUpdate()
        {
            var deck = PlayData.Instance.Value?.deck;
            bool visible = Plugin.Enabled.Value && deck != null && anchorTarget != null && anchorTarget.gameObject.activeInHierarchy;
            group.alpha = visible ? 1f : 0f;
            if (!visible) return;

            Reposition();

            // 0.25초마다 집계 (아이템은 30~50개 수준이라 부담 없음)
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return;
            timer = 0.25f;

            for (int i = 0; i < Cells.Length; i++)
            {
                var type = Cells[i].type;
                int count = deck.Count(c => SafeType(c) == type);
                if (count == lastCounts[i]) continue;
                lastCounts[i] = count;
                texts[i].text = $"{Cells[i].label} <b>{count}</b>";
            }
        }

        /// <summary>"아이템" 아이콘 바로 아래 가운데. 화면 밖으로 나가면 안쪽으로 당김</summary>
        private void Reposition()
        {
            var rt = (RectTransform)transform;
            var parent = (RectTransform)rt.parent;
            var corners = new Vector3[4];
            anchorTarget.GetWorldCorners(corners); // 0: 왼쪽 아래, 3: 오른쪽 아래
            Vector2 local = parent.InverseTransformPoint((corners[0] + corners[3]) * 0.5f);
            Vector2 pos = local - parent.rect.center + new Vector2(0f, -Gap) + Plugin.ExtraOffset.Value;

            float halfW = rt.sizeDelta.x * 0.5f;
            float maxX = parent.rect.width * 0.5f - halfW - 4f;
            pos.x = Mathf.Clamp(pos.x, -maxX, maxX);
            rt.anchoredPosition = pos;
        }

        private static ItemType SafeType(CardData c)
        {
            try { return c.ItemBehavior.ItemType; } catch { return ItemType.None; }
        }
    }
}
