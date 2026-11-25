using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PocketLegend.InventoryFit
{
    /// <summary>
    /// 전투 인벤토리가 커졌을 때(최대 7x7) 위쪽 유물, 오른쪽 캐릭터/체력바를 가리는 문제를 줄인다.
    ///  - Alt 키로 전투 인벤토리(안의 아이템 포함)를 반투명하게 + 클릭이 통과되게 해서 가려진 것을 볼 수 있게 함
    ///  - 방식 2가지: Hold(누르고 있는 동안) / Toggle(누를 때마다 전환)
    ///  - 오른쪽 위 "나의 인벤토리" 화면에 사용법 안내 문구와 방식 전환 버튼을 표시
    ///
    /// 0.2.0: 자동 축소(사이즈 조절)와 "유물 위 자동 반투명"은 사용자 결정으로 보류 → 코드에서 삭제.
    ///        (0.1.0 코드는 커밋 전이라 별도 사본으로 보관)
    /// </summary>
    [BepInPlugin(Guid, "Inventory Fit", "0.2.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "pocketlegend.inventoryfit";
        // 모드 관리(Mod Switch)가 리플렉션으로 읽는 표시 정보
        public const string ModCategory = "UX";
        public const string NameKo = "인벤토리 맞춤";
        public const bool SingleOnly = false;
        public const string MenuOptions = "Options/PeekMode";

        public const string ModeHold = "Hold";
        public const string ModeToggle = "Toggle";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyboardShortcut> PeekKey;
        internal static ConfigEntry<string> PeekMode;
        internal static ConfigEntry<float> PeekAlpha;
        internal static ConfigEntry<bool> ShowGuide;
        internal static ConfigEntry<Vector2> GuideOffset;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "모드 사용 여부 (타이틀 화면의 모드 관리에서도 변경 가능)");
            PeekKey = Config.Bind("Options", "PeekKey", new KeyboardShortcut(KeyCode.LeftAlt), "전투 인벤토리를 반투명하게 하는 키");
            PeekMode = Config.Bind("Options", "PeekMode", ModeHold, new ConfigDescription(
                "Hold = 누르고 있는 동안만 반투명 / Toggle = 누를 때마다 반투명 ↔ 원래대로 ('나의 인벤토리' 화면 버튼으로도 변경 가능)",
                new AcceptableValueList<string>(ModeHold, ModeToggle)));
            PeekAlpha = Config.Bind("Options", "PeekAlpha", 0.25f, new ConfigDescription("반투명일 때 불투명도", new AcceptableValueRange<float>(0f, 1f)));
            ShowGuide = Config.Bind("Guide", "ShowGuide", true, "'나의 인벤토리' 화면에 사용법 안내와 방식 버튼 표시");
            GuideOffset = Config.Bind("Guide", "ExtraOffset", Vector2.zero, "안내 위치 추가 이동값 (px)");
            gameObject.AddComponent<Peeker>();
        }

        /// <summary>모드 관리의 세부 옵션 줄에 보일 문구 (value 가 null 이면 항목 이름)</summary>
        public static string MenuText(string key, string value, bool korean)
        {
            if (key != "PeekMode") return null;
            if (value == null) return korean ? "Alt 동작" : "Alt mode";
            return ModeText(value, korean);
        }

        internal static string ModeText(string mode, bool korean) => mode == ModeToggle
            ? (korean ? "누를 때마다 전환 (Toggle)" : "Toggle")
            : (korean ? "누르고 있는 동안 (Hold)" : "Hold");

        internal static bool IsToggle => PeekMode.Value == ModeToggle;

        internal static string KeyName
        {
            get
            {
                var key = PeekKey.Value.MainKey;
                return key == KeyCode.LeftAlt || key == KeyCode.RightAlt ? "Alt" : key.ToString();
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

        // PlayerInventoryUI 는 처음 열릴 때 만들어지므로 찾을 때까지 매 프레임 확인
        private PlayerInventoryUI attached;
        private void Update()
        {
            var ui = Getable.Get<PlayerInventoryUI>();
            if (ui == null || ui == attached) return;
            attached = ui;
            try { Guide.Create(ui); }
            catch (Exception e) { Log.LogError(e); }
        }
    }

    /// <summary>Alt 키 상태에 따라 전투 인벤토리를 반투명하게</summary>
    internal class Peeker : MonoBehaviour
    {
        private InventoryUI target;
        private CanvasGroup group;
        private bool groupAdded;
        private float originalAlpha = 1f;
        private bool originalRaycast = true;
        private bool peeking;

        private bool toggled;  // Toggle 방식에서 현재 켜져 있는지
        private bool armed;    // Toggle 방식: 키를 누른 뒤 다른 키 없이 뗐는지 확인용

        private void LateUpdate()
        {
            try { Tick(); }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
                enabled = false; // 매 프레임 오류가 쌓이지 않게 이 기능만 멈춤
            }
        }

        private void Tick()
        {
            var battle = Getable.Get<InventoryBattleUI>();
            var inv = battle != null && battle.isActiveAndEnabled ? battle.inventoryUI : null;
            if (!Plugin.Enabled.Value || inv == null || !inv.gameObject.activeInHierarchy)
            {
                Restore();
                toggled = false;
                armed = false;
                return;
            }
            if (inv != target)
            {
                Restore();
                target = inv;
            }

            bool on;
            if (Plugin.IsToggle)
            {
                var key = Plugin.PeekKey.Value.MainKey;
                if (Input.GetKeyDown(key)) armed = true;
                else if (armed && Input.anyKeyDown) armed = false; // Alt+Tab 처럼 다른 키와 같이 누른 경우는 전환하지 않음
                if (Input.GetKeyUp(key))
                {
                    if (armed && Application.isFocused) toggled = !toggled;
                    armed = false;
                }
                on = toggled;
            }
            else
            {
                toggled = false;
                on = Plugin.PeekKey.Value.IsPressed();
            }

            // 아이템을 드래그하는 동안에는 원래대로 (놓을 자리가 잘 보이게)
            Apply(on && PickUI.Current == null);
        }

        private void Apply(bool want)
        {
            if (want == peeking) return;
            peeking = want;
            if (!want) { Restore(); return; }

            group = target.GetComponent<CanvasGroup>();
            groupAdded = group == null;
            if (groupAdded) group = target.gameObject.AddComponent<CanvasGroup>();
            originalAlpha = group.alpha;
            originalRaycast = group.blocksRaycasts;
            group.alpha = originalAlpha * Plugin.PeekAlpha.Value;
            group.blocksRaycasts = false; // 마우스가 인벤토리를 통과해서 아래 유물/캐릭터에 닿게
        }

        private void Restore()
        {
            peeking = false;
            if (group == null) return;
            if (groupAdded) Destroy(group);
            else
            {
                group.alpha = originalAlpha;
                group.blocksRaycasts = originalRaycast;
            }
            group = null;
        }
    }

    /// <summary>"나의 인벤토리" 화면의 사용법 안내 + 방식 전환 버튼</summary>
    internal class Guide : MonoBehaviour
    {
        private const float Width = 620f, Gap = 14f;
        private PlayerInventoryUI owner;
        private GameObject content;
        private TextMeshProUGUI text;
        private TextMeshProUGUI buttonText;
        private string lastKey;
        private readonly Vector3[] corners = new Vector3[4];

        public static void Create(PlayerInventoryUI ui)
        {
            var parent = (RectTransform)ui.transform;
            var fontSource = ui.GetComponentInChildren<TMP_Text>(true);
            if (fontSource == null)
            {
                var resource = Getable.Get<ResourceUI>();
                fontSource = resource != null ? resource.GetComponentInChildren<TMP_Text>(true) : null;
            }

            var root = new GameObject("InventoryFit_Guide", typeof(RectTransform));
            var rt = (RectTransform)root.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(Width, 110f);
            rt.SetAsLastSibling();

            var guide = root.AddComponent<Guide>();
            guide.owner = ui;
            guide.content = root;

            // 안내 문구
            guide.text = CreateText(rt, "Text", fontSource, 21f);
            var trt = guide.text.rectTransform;
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.sizeDelta = new Vector2(0f, 60f);
            trt.anchoredPosition = Vector2.zero;
            guide.text.enableWordWrapping = true;

            // 방식 전환 버튼 (BetterCardSkip 의 버튼과 같은 모양)
            var buttonGo = new GameObject("ModeButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
            var brt = (RectTransform)buttonGo.transform;
            brt.SetParent(rt, false);
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.sizeDelta = new Vector2(380f, 42f);
            brt.anchoredPosition = new Vector2(0f, -64f);
            buttonGo.GetComponent<Image>().color = new Color(0.10f, 0.08f, 0.06f, 0.9f);
            var outline = buttonGo.GetComponent<Outline>();
            outline.effectColor = new Color(0.85f, 0.70f, 0.35f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
            var button = buttonGo.GetComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(1.4f, 1.3f, 1.1f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                Plugin.PeekMode.Value = Plugin.IsToggle ? Plugin.ModeHold : Plugin.ModeToggle; // 설정 파일에 바로 저장됨
                Plugin.Log.LogInfo("Alt 동작 => " + Plugin.PeekMode.Value);
            });

            guide.buttonText = CreateText(brt, "Text", fontSource, 20f);
            var btrt = guide.buttonText.rectTransform;
            btrt.anchorMin = Vector2.zero;
            btrt.anchorMax = Vector2.one;
            btrt.offsetMin = btrt.offsetMax = Vector2.zero;
            guide.buttonText.color = new Color(1f, 0.85f, 0.45f);

            Plugin.Log.LogInfo("'나의 인벤토리' 화면에 안내 추가");
        }

        private static TextMeshProUGUI CreateText(RectTransform parent, string name, TMP_Text fontSource, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            if (fontSource != null && fontSource.font != null)
            {
                tmp.font = fontSource.font;
                tmp.fontSharedMaterial = fontSource.font.material;
            }
            tmp.fontSize = size;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            return tmp;
        }

        private void LateUpdate()
        {
            bool show = Plugin.Enabled.Value && Plugin.ShowGuide.Value;
            for (int i = 0; i < transform.childCount; i++)
                transform.GetChild(i).gameObject.SetActive(show);
            if (!show) return;

            bool ko = Plugin.IsKorean;
            string key = $"{ko}|{Plugin.PeekMode.Value}|{Plugin.KeyName}";
            if (key != lastKey)
            {
                lastKey = key;
                string k = $"<color=#FFD27F><b>{Plugin.KeyName}</b></color>";
                text.text = Plugin.IsToggle
                    ? (ko ? $"{k} 키를 누를 때마다 전투 인벤토리가 투명 / 원래대로 바뀝니다.\n(투명할 때는 가려진 유물·캐릭터를 볼 수 있고, 클릭이 인벤토리를 통과합니다)"
                          : $"Press {k} to switch the battle inventory see-through on / off.\n(While see-through, you can see and click what is behind it)")
                    : (ko ? $"{k} 키를 누르고 있는 동안 전투 인벤토리가 투명해집니다.\n(가려진 유물·캐릭터를 볼 수 있고, 클릭이 인벤토리를 통과합니다)"
                          : $"Hold {k} to make the battle inventory see-through.\n(You can see and click what is behind it)");
                buttonText.text = (ko ? "방식 : " : "Mode : ") + Plugin.ModeText(Plugin.PeekMode.Value, ko);
            }
            Reposition();
        }

        /// <summary>"나의 인벤토리"의 인벤토리 칸 바로 아래 가운데. 화면 밖으로 나가지 않게 안쪽으로 당김</summary>
        private void Reposition()
        {
            var rt = (RectTransform)transform;
            var parent = (RectTransform)rt.parent;
            var inv = owner != null ? owner.inventoryUI : null;
            Transform source = inv != null ? (HasActiveChild(inv.bezelParent) ? inv.bezelParent : inv.elementParent) : null;

            Vector2 local = new Vector2(parent.rect.center.x, parent.rect.yMin + rt.sizeDelta.y + 40f);
            if (source != null && HasActiveChild(source))
            {
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue;
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
                }
                local = new Vector2((minX + maxX) * 0.5f, minY - Gap);
            }

            Vector2 pos = local - parent.rect.center + Plugin.GuideOffset.Value;
            Rect pr = parent.rect;
            Vector2 size = rt.sizeDelta;
            // 부모가 안내보다 클 때만 (화면 전체 크기 패널일 때) 안쪽으로 당김
            if (pr.width > size.x && pr.height > size.y)
            {
                float halfX = pr.width * 0.5f - size.x * 0.5f - 4f;
                float minPosY = -pr.height * 0.5f + size.y + 4f; // 피벗이 위쪽이라 아래로 size.y 만큼 차지
                pos.x = Mathf.Clamp(pos.x, -halfX, halfX);
                pos.y = Mathf.Max(pos.y, minPosY);
            }
            rt.anchoredPosition = pos;
        }

        private static bool HasActiveChild(Transform t)
        {
            if (t == null) return false;
            for (int i = 0; i < t.childCount; i++)
                if (t.GetChild(i).gameObject.activeSelf) return true;
            return false;
        }
    }
}
