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
    ///
    /// 0.2.0: "아이템" 아이콘 오른쪽 아래에 이미지와 겹치게 동그라미 배지 + 총 보유 개수(위 4가지 유형의 합)
    /// </summary>
    [BepInPlugin(Guid, "Item Type Count", "0.2.2")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "pocketlegend.itemcount";
        // 모드 관리(Mod Switch)가 리플렉션으로 읽는 표시 정보: 분류(UX=조작 편의, UI=정보 표시, BAL=밸런스 변경) / 한국어 이름 / 협동 플레이 미적용
        public const string ModCategory = "UI";
        public const string NameKo = "유형별 개수";
        public const bool SingleOnly = false;
        public const string MenuOptions = "Display/ShowTotalBadge";
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<Vector2> ExtraOffset;
        internal static ConfigEntry<bool> ShowTotalBadge;
        internal static ConfigEntry<Vector2> BadgeOffset;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "모드 사용 여부 (타이틀 화면의 모드 관리에서도 변경 가능)");
            ExtraOffset = Config.Bind("UI", "ExtraOffset", Vector2.zero, "박스 추가 이동값 (px, 위치가 어색할 때 조정)");
            ShowTotalBadge = Config.Bind("Display", "ShowTotalBadge", true, "'아이템' 아이콘 오른쪽 아래에 총 보유 개수 동그라미 표시");
            BadgeOffset = Config.Bind("UI", "BadgeOffset", Vector2.zero, "동그라미 추가 이동값 (px)");
        }

        /// <summary>모드 관리의 세부 옵션 줄에 보일 문구 (value 가 null 이면 항목 이름)</summary>
        public static string MenuText(string key, string value, bool korean)
        {
            if (value != null || key != "ShowTotalBadge") return null;
            return korean ? "총 개수 동그라미" : "Total count badge";
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
        private GameObject badge;
        private TextMeshProUGUI badgeText;
        private int lastTotal = -1;
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
            box.CreateBadge(font);
            Plugin.Log.LogInfo("유형별 아이템 수 박스 생성");
        }

        private void LateUpdate()
        {
            var deck = PlayData.Instance.Value?.deck;
            bool visible = Plugin.Enabled.Value && deck != null && anchorTarget != null && anchorTarget.gameObject.activeInHierarchy;
            group.alpha = visible ? 1f : 0f;
            if (badge != null)
            {
                bool showBadge = visible && Plugin.ShowTotalBadge.Value;
                if (badge.activeSelf != showBadge) badge.SetActive(showBadge);
                if (showBadge) ((RectTransform)badge.transform).anchoredPosition = BadgeCorner + Plugin.BadgeOffset.Value;
            }
            if (!visible) return;

            Reposition();

            // 0.25초마다 집계 (아이템은 30~50개 수준이라 부담 없음)
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return;
            timer = 0.25f;

            int total = 0;
            for (int i = 0; i < Cells.Length; i++)
            {
                var type = Cells[i].type;
                int count = deck.Count(c => SafeType(c) == type);
                total += count;
                if (count == lastCounts[i]) continue;
                lastCounts[i] = count;
                texts[i].text = $"{Cells[i].label} <b>{count}</b>";
            }
            // 동그라미 안 숫자 = 표의 4가지 유형 합 (예: 5, 4, 0, 3 → 12)
            if (badgeText != null && total != lastTotal)
            {
                lastTotal = total;
                badgeText.text = total.ToString();
            }
        }

        private const float BadgeSize = 68f; // 0.2.0 의 34 에서 2배 (숫자가 잘 보이도록)
        // 동그라미의 오른쪽 아래 끝 위치 (아이콘 오른쪽 아래 모서리 기준). 이 점을 고정하고 왼쪽 위로 커지게 함
        private static readonly Vector2 BadgeCorner = new Vector2(9f, -9f);
        private static Sprite circleSprite;

        /// <summary>
        /// "아이템" 아이콘(deckUI)의 자식으로 동그라미 배지를 붙임 → 아이콘과 함께 움직이고, 아이콘 위에 그려짐
        /// </summary>
        private void CreateBadge(TMP_Text font)
        {
            var parent = anchorTarget;
            badge = new GameObject("ItemCountBadge", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)badge.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f); // 아이콘 오른쪽 아래 모서리 기준
            rt.pivot = new Vector2(1f, 0f); // 피벗을 오른쪽 아래로 → 크기를 키우면 왼쪽 위(아이콘 쪽)로 커짐
            rt.sizeDelta = new Vector2(BadgeSize, BadgeSize);
            rt.anchoredPosition = BadgeCorner;
            rt.SetAsLastSibling();

            var img = badge.GetComponent<Image>();
            img.sprite = CircleSprite();
            img.color = Color.white;   // 색은 그림 자체에 들어 있음 (테두리 금색 + 안쪽 어두운 색)
            img.raycastTarget = false; // 아이콘 클릭을 막지 않도록

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var trt = (RectTransform)textGo.transform;
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(6f, 0f);
            trt.offsetMax = new Vector2(-6f, 0f);
            badgeText = textGo.GetComponent<TextMeshProUGUI>();
            if (font != null)
            {
                badgeText.font = font.font;
                badgeText.fontSharedMaterial = font.font != null ? font.font.material : font.fontSharedMaterial;
            }
            badgeText.fontStyle = FontStyles.Bold;
            badgeText.alignment = TextAlignmentOptions.Center;
            badgeText.color = Color.white;
            badgeText.enableWordWrapping = false;
            badgeText.raycastTarget = false;
            // 보통 34 크기, 세 자리 수(100 이상)일 때만 동그라미 밖으로 나가지 않게 조금 줄어듦
            badgeText.enableAutoSizing = true;
            badgeText.fontSizeMin = 24f;
            badgeText.fontSizeMax = 34f;
        }

        /// <summary>
        /// 동그라미 그림을 코드로 직접 그림 (게임 안에서 쓸 만한 원 이미지를 찾을 필요 없이 어디서나 같은 모양).
        /// 64x64 텍스처에 금색 테두리 + 어두운 안쪽을 칠하고, 가장자리는 반투명으로 부드럽게(안티에일리어싱).
        /// </summary>
        private static Sprite CircleSprite()
        {
            if (circleSprite != null) return circleSprite;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var fill = new Color(0.10f, 0.07f, 0.05f, 0.95f);
            var ring = new Color(0.85f, 0.70f, 0.35f, 1f);
            float radius = size * 0.5f - 1f, ringWidth = 4.5f;
            var center = new Vector2(size * 0.5f - 0.5f, size * 0.5f - 0.5f);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), center);
                    float outer = Mathf.Clamp01(radius - d + 0.5f);             // 원 바깥 가장자리를 부드럽게
                    float inner = Mathf.Clamp01(radius - ringWidth - d + 0.5f); // 1 = 안쪽, 0 = 테두리
                    var c = Color.Lerp(ring, fill, inner);
                    c.a *= outer;
                    pixels[y * size + x] = c;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return circleSprite;
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
