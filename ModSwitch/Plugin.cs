using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;

namespace PocketLegend.ModSwitch
{
    /// <summary>
    /// 타이틀 메뉴의 "게임종료" 바로 위에 "모드 관리" 메뉴를 추가한다.
    /// 설정에 [General] Enabled (bool) 항목을 가진 플러그인이 자동으로 목록에 나타난다.
    ///
    /// 0.4.0 (게임 빌드 26896844 대응)
    ///  - 업데이트로 "도감"(collectionButton) 추가, "크레딧" 숨김, "협동 플레이"가 나중에 동적으로 추가됨
    ///    (TitleUI.AddCoopButton 이 버튼들을 한 칸씩 위로 밀어 올림)
    ///  - 그래서 버튼 이름을 고정으로 나열하지 않고, 매 프레임 "실제로 보이는 메뉴 버튼"을 읽어
    ///    위에서부터 같은 간격으로 다시 쌓는다. (모드 관리는 항상 게임종료 바로 위)
    ///
    /// 0.5.0
    ///  - 게임 언어 설정(Option.languageKey)이 한국어면 한국어, 그 외에는 영어로 표시
    ///  - 각 모드 Plugin 클래스의 표시 정보(ModCategory / NameKo / SingleOnly 상수)를 리플렉션으로 읽어
    ///    "UX_아이템 정렬", "BAL_강화 비용 고정 [싱글 전용]" 처럼 표시 (정보가 없는 다른 모드는 이름 그대로)
    ///  - 모드가 많아도 화면 밖으로 내려가지 않도록, 원래 메뉴가 차지하던 칸 수 안에서 페이지로 나눔
    ///  - 이름이 길면 줄바꿈 없이 글자 크기를 줄여 한 줄에 맞춤
    ///
    /// 0.6.0
    ///  - 모드별 세부 옵션 줄 (MenuOptions / MenuText 규칙, 아래 CollectRows 참고)
    ///  - 목록 줄 간격을 0.75칸으로 줄여 한 페이지에 더 많이 표시
    ///
    /// 0.6.1
    ///  - 분류 3가지 정렬: UX(조작 편의) → UI(정보 표시) → BAL(밸런스 변경)
    ///  - 타이틀 자체 테스트(스크린샷)는 개발용 빌드(-p:ModDev=true)에만 포함
    /// </summary>
    [BepInPlugin(Guid, "Mod Switch", "0.6.1")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "pocketlegend.modswitch";
        internal static ManualLogSource Log;

        internal static ConfigEntry<string> Language;
#if MODDEV
        internal static ConfigEntry<bool> SelfTest;
#endif

        private void Awake()
        {
            Log = Logger;
            Language = Config.Bind("Display", "Language", "Auto", new ConfigDescription(
                "모드 관리 표시 언어. Auto = 게임 언어 설정을 따름(한국어면 한국어, 그 외는 영어) / Korean / English",
                new AcceptableValueList<string>("Auto", "Korean", "English")));
#if MODDEV
            // 개발용 빌드(dotnet build -p:ModDev=true)에만 들어감. 배포용 빌드에는 설정 항목도 생기지 않음
            SelfTest = Config.Bind("Debug", "TitleSelfTest", false,
                "개발용: 타이틀에서 메뉴/모드 관리 화면을 자동으로 열고 닫으며 BepInEx 폴더에 스크린샷(selftest_*.png)을 남김. 끝나면 자동으로 false 로 돌아감");
#endif
            new Harmony(Guid).PatchAll(typeof(Patches));
        }

        // 타이틀 화면이 플러그인 로드보다 먼저 떠 있는 경우 + 협동 플레이 버튼이 늦게 추가되는 경우를 대비해 매 프레임 확인
        private void Update()
        {
            var title = Getable.Get<TitleUI>();
            if (title == null || !title.gameObject.activeInHierarchy) return;
            ModMenu.Ensure(title);
            ModMenu.Layout();
            ModMenu.RefreshLanguage();
#if MODDEV
            if (SelfTest.Value && !selfTestRunning) StartCoroutine(RunSelfTest());
#endif
        }

#if MODDEV
        private bool selfTestRunning;

        /// <summary>게임 내부 렌더링을 직접 캡처하므로 다른 창에 가려져도 결과가 남는다</summary>
        private System.Collections.IEnumerator RunSelfTest()
        {
            selfTestRunning = true;
            string dir = Paths.BepInExRootPath;
            yield return new WaitForSecondsRealtime(6f); // 협동 플레이 버튼이 붙을 시간
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "selftest_1_title.png"));
            yield return new WaitForSecondsRealtime(1f);
            ModMenu.OpenListForTest();
            yield return new WaitForSecondsRealtime(1.5f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "selftest_2_list.png"));
            yield return new WaitForSecondsRealtime(1f);
            for (int p = 2; p <= ModMenu.PageCount; p++)
            {
                ModMenu.NextPageForTest();
                yield return new WaitForSecondsRealtime(1.5f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"selftest_2_list_page{p}.png"));
                yield return new WaitForSecondsRealtime(1f);
            }
            ModMenu.CloseList();
            yield return new WaitForSecondsRealtime(1.5f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "selftest_3_back.png"));
            yield return new WaitForSecondsRealtime(1f);
            Log.LogInfo("SelfTest 완료: " + ModMenu.DescribeKeyboardOrder());
            SelfTest.Value = false;
        }
#endif
    }

    internal static class Patches
    {
        [HarmonyPostfix, HarmonyPatch(typeof(TitleUI), nameof(TitleUI.Show))]
        private static void TitleUI_Show(TitleUI __instance)
        {
            ModMenu.CloseList(); // 게임에서 타이틀로 돌아왔을 때는 기본 메뉴 상태로
            ModMenu.Ensure(__instance);
            ModMenu.Layout();
        }
    }

    internal static class ModMenu
    {
        private static readonly FieldInfo MenualSelectionsField = AccessTools.Field(typeof(TitleUI), "menualSelections");
        private static readonly PropertyInfo ActiveSelectionsProp = AccessTools.Property(typeof(TitleUI), "ActiveMenualSelections");
        private static readonly MethodInfo OnStateMethod = AccessTools.Method(typeof(TitleUI), "OnState");

        private static TitleUI title;
        private static ButtonUI menuButton;
        private static float step;               // 메뉴 한 칸 간격 (게임 원래 배치에서 1회 측정)
        private static bool loggedLayout;
        private static bool? shownKorean;        // 메뉴 버튼에 마지막으로 표시한 언어

        // 목록 모드 상태
        private static readonly List<GameObject> hiddenOriginals = new List<GameObject>();
        private static readonly List<ButtonUI> listRows = new List<ButtonUI>();
        private static List<MenualSelectionUI> savedActive;
        private static Vector2 listTop;          // 목록 첫 줄 위치
        private static int listSlots;            // 목록이 쓸 수 있는 칸 수 (= 원래 메뉴가 차지하던 칸 수)
        private static int page;                 // 현재 페이지 (0부터)
        internal static int PageCount { get; private set; } = 1;

        /// <summary>필드가 없는 게임 버전에서도 죽지 않도록 null 허용</summary>
        private static ButtonUI GetButton(TitleUI t, string field) =>
            AccessTools.Field(typeof(TitleUI), field)?.GetValue(t) as ButtonUI;

        private static List<MenualSelectionUI> MenualSelections(TitleUI t) =>
            (List<MenualSelectionUI>)MenualSelectionsField.GetValue(t);

        private static List<MenualSelectionUI> ActiveSelections(TitleUI t) =>
            (List<MenualSelectionUI>)ActiveSelectionsProp.GetValue(t);

        /// <summary>복제 원본: 도감(신규) → 크레딧(구버전) → 도전기록 순으로 있는 것</summary>
        private static ButtonUI CloneSource(TitleUI t) =>
            GetButton(t, "collectionButton") ?? GetButton(t, "creditButton") ?? GetButton(t, "recordButton");

        public static void Ensure(TitleUI t)
        {
            if (menuButton != null && title == t) return;
            try
            {
                title = t;
                var exit = GetButton(t, "exitButton");
                var source = CloneSource(t);
                if (exit == null || source == null) { Plugin.Log.LogWarning("타이틀 메뉴 버튼을 찾지 못함"); return; }

                // 우리가 아무것도 옮기기 전에 게임 원래 배치에서 한 칸 간격 측정
                step = MeasureStep(GameButtons(exit.transform.parent));

                menuButton = CloneTitleButton(source, Text.MenuTitle, OpenList);
                menuButton.gameObject.name = "ModSwitch_MenuButton";
                menuButton.transform.SetParent(exit.transform.parent, false);
                loggedLayout = false;
                shownKorean = Text.IsKorean;
                Plugin.Log.LogInfo($"'모드 관리' 메뉴 추가됨 (원본: {source.name}, 간격 {step:0.#})");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }

        /// <summary>옵션에서 언어를 바꾸면 메뉴 이름도 따라 바꿈</summary>
        public static void RefreshLanguage()
        {
            if (menuButton == null) return;
            bool korean = Text.IsKorean;
            if (shownKorean == korean) return;
            shownKorean = korean;
            SetText(menuButton, Text.MenuTitle);
        }

        /// <summary>컨테이너 안에서 현재 보이는 게임 메뉴 버튼 (우리 버튼 제외), 위→아래 순</summary>
        private static List<RectTransform> GameButtons(Transform container)
        {
            var list = new List<RectTransform>();
            for (int i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i);
                if (!child.gameObject.activeSelf || child.GetComponent<ButtonUI>() == null) continue;
                if (menuButton != null && child == menuButton.transform) continue;
                if (listRows.Any(r => r != null && r.transform == child)) continue;
                list.Add((RectTransform)child);
            }
            list.Sort((a, b) => b.anchoredPosition.y.CompareTo(a.anchoredPosition.y));
            return list;
        }

        /// <summary>인접 버튼 사이 간격 중 가장 작은 값 = 한 칸 (숨겨진 크레딧 자리 같은 빈칸은 2칸으로 보이므로 제외됨)</summary>
        private static float MeasureStep(List<RectTransform> buttons)
        {
            float min = float.MaxValue;
            for (int i = 1; i < buttons.Count; i++)
            {
                float gap = buttons[i - 1].anchoredPosition.y - buttons[i].anchoredPosition.y;
                if (gap > 1f && gap < min) min = gap;
            }
            return min == float.MaxValue ? 64f : min;
        }

        /// <summary>
        /// 보이는 메뉴 버튼을 위에서부터 한 칸 간격으로 다시 쌓고, "모드 관리"를 게임종료 바로 위에 넣는다.
        /// 위치가 이미 맞으면 아무것도 바꾸지 않으므로 매 프레임 호출해도 됨.
        /// </summary>
        public static void Layout()
        {
            if (title == null || menuButton == null || listRows.Count > 0) return; // 목록 모드 중에는 건드리지 않음
            try
            {
                var exit = GetButton(title, "exitButton");
                if (exit == null) return;
                var ordered = GameButtons(exit.transform.parent);
                if (ordered.Count == 0) return;

                int exitIndex = ordered.IndexOf((RectTransform)exit.transform);
                ordered.Insert(exitIndex >= 0 ? exitIndex : ordered.Count, (RectTransform)menuButton.transform);

                var top = ordered[0].anchoredPosition;
                bool moved = false;
                for (int i = 0; i < ordered.Count; i++)
                {
                    var target = new Vector2(top.x, top.y - step * i);
                    if ((ordered[i].anchoredPosition - target).sqrMagnitude > 0.25f)
                    {
                        ordered[i].anchoredPosition = target;
                        moved = true;
                    }
                }

                SyncKeyboardOrder(exit);

                if (moved && !loggedLayout)
                {
                    loggedLayout = true;
                    Plugin.Log.LogInfo("메뉴 배치: " + string.Join(" / ", ordered.Select(r => r.name)));
                }
                else if (moved) loggedLayout = false; // 협동 플레이 추가 등으로 다시 바뀌면 한 번 더 기록
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }

        /// <summary>키보드 이동 순서도 화면 순서와 같게: "모드 관리"를 게임종료 바로 앞에</summary>
        private static void SyncKeyboardOrder(ButtonUI exit)
        {
            var mine = menuButton.GetComponent<MenualSelectionUI>();
            var exitSel = exit.GetComponent<MenualSelectionUI>();
            var list = MenualSelections(title);
            if (mine == null || list == null) return;

            int want = exitSel != null ? list.IndexOf(exitSel) : -1;
            int cur = list.IndexOf(mine);
            if (cur >= 0 && want >= 0 && cur == want - 1) return; // 이미 올바른 위치

            if (cur >= 0) list.RemoveAt(cur);
            want = exitSel != null ? list.IndexOf(exitSel) : -1;
            list.Insert(want >= 0 ? want : list.Count, mine);

            if (ActiveSelections(title) != null)
                ActiveSelectionsProp.SetValue(title, list
                    .Where(x => x != null && x.gameObject.activeInHierarchy && !x.GetComponent<ButtonUI>().IsDisable)
                    .ToList());
        }

        /// <summary>
        /// 타이틀 메뉴 버튼을 복제해서 폰트/외곽선/호버/키보드 커서가 동일한 버튼을 만든다.
        /// (게임도 협동 플레이 버튼을 같은 방식으로 만든다: TitleUI.AddCoopButton)
        /// </summary>
        private static ButtonUI CloneTitleButton(ButtonUI source, string text, Action onClick)
        {
            var go = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent, false);

            // TextUISetter: Start 에서 원래 문구로 덮어쓰므로 제거
            foreach (var setter in go.GetComponentsInChildren<TextUISetter>(true))
                UnityEngine.Object.DestroyImmediate(setter);

            // 복제본은 원본과 같은 재질 인스턴스를 참조하므로, 호버 시 외곽선 색 변경이 원본에도 번진다 → 재질 분리
            foreach (var tmp in go.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (tmp.fontSharedMaterial != null)
                    tmp.fontSharedMaterial = new Material(tmp.fontSharedMaterial);

            var button = go.GetComponent<ButtonUI>();
            button.OnClick = onClick;
            var owner = title;
            button.OnSetState = (state, b) => OnStateMethod.Invoke(owner, new object[] { state, b });

            // 키보드 선택: 직렬화된 이벤트는 원래 버튼 동작을 가리킬 수 있으므로 새로 연결
            var sel = go.GetComponent<MenualSelectionUI>();
            if (sel != null)
            {
                SetEvent(sel, "onClick", () => button.Click());
                SetEvent(sel, "onEnter", () => button.Enter());
                SetEvent(sel, "onExit", () => button.Exit());
            }

            go.SetActive(true);
            SetText(button, text);
            button.SetDisable(false);
            return button;
        }

        private static void SetEvent(MenualSelectionUI sel, string field, UnityAction action)
        {
            var ev = new UnityEvent();
            ev.AddListener(action);
            AccessTools.Field(typeof(MenualSelectionUI), field).SetValue(sel, ev);
        }

#if MODDEV
        internal static void OpenListForTest() => OpenList();
        internal static void NextPageForTest() => ShowPage(page + 1);
#endif

        /// <summary>키보드 이동 순서를 버튼 이름으로 (자체 테스트 로그용)</summary>
        internal static string DescribeKeyboardOrder()
        {
            var active = title != null ? ActiveSelections(title) : null;
            return active == null ? "(없음)" : "키보드 순서 = " + string.Join(" / ", active.Where(x => x != null).Select(x => x.name));
        }

        /// <summary>"모드 관리" 클릭: 원래 메뉴를 숨기고 목록 첫 페이지를 보여줌</summary>
        private static void OpenList()
        {
            if (title == null || menuButton == null) return;
            CloseList();

            // 현재 메뉴의 맨 위 위치와 칸 수를 기억 (목록은 이 칸 수 안에서만 쌓으므로 화면 밖으로 넘어가지 않음)
            var container = menuButton.transform.parent;
            var visible = GameButtons(container);
            visible.Add((RectTransform)menuButton.transform);
            listTop = visible.OrderByDescending(r => r.anchoredPosition.y).First().anchoredPosition;
            // 목록 줄은 원래 메뉴보다 촘촘하게(0.75칸) 쌓되, 원래 메뉴가 차지하던 높이는 넘지 않음
            listStep = step * ListStepRatio;
            listSlots = Math.Max(Mathf.FloorToInt((visible.Count - 1) * step / listStep + 0.001f) + 1, 4);

            // 원래 메뉴 버튼들을 잠시 숨김
            for (int i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i).gameObject;
                if (child.activeSelf && child.GetComponent<ButtonUI>() != null)
                {
                    hiddenOriginals.Add(child);
                    child.SetActive(false);
                }
            }

            savedActive = ActiveSelections(title);
            ShowPage(0);
        }

        /// <summary>목록의 한 페이지를 만든다. 칸이 모자라면 "다음 페이지" + "돌아가기" 두 칸을 남기고 나눔</summary>
        private static void ShowPage(int requested)
        {
            bool keyboard = MenualSelectionUI.IsMenual;
            MenualSelectionUI.Clear();
            DestroyRows();

            var pages = BuildPages(CollectGroups());
            PageCount = pages.Count;
            page = ((requested % PageCount) + PageCount) % PageCount; // 마지막 페이지에서 "다음"을 누르면 첫 페이지로

            var container = menuButton.transform.parent;
            var source = CloneSource(title);
            var scales = new List<float>();
            foreach (var entry in pages[page])
            {
                ButtonUI row = null;
                var e = entry;
                row = CloneTitleButton(source, e.Label(), () =>
                {
                    e.Toggle(); // 설정 파일에 즉시 저장됨
                    Plugin.Log.LogInfo("모드 관리: " + e.Label());
                    SetText(row, e.Label());
                });
                listRows.Add(row);
                scales.Add(e.IsOption ? OptionTextScale : ModTextScale);
            }
            if (PageCount > 1)
            {
                listRows.Add(CloneTitleButton(source, Text.NextPage(page + 1, PageCount), () => ShowPage(page + 1)));
                scales.Add(ModTextScale);
            }
            listRows.Add(CloneTitleButton(source, Text.Back, CloseList));
            scales.Add(ModTextScale);

            for (int i = 0; i < listRows.Count; i++)
            {
                var rt = (RectTransform)listRows[i].transform;
                rt.SetParent(container, false);
                rt.anchoredPosition = new Vector2(listTop.x, listTop.y - listStep * i);
                // 촘촘하게 쌓은 만큼 클릭 영역 높이도 줄여서 위아래 줄과 겹치지 않게
                if (Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y) && rt.sizeDelta.y > listStep)
                    rt.sizeDelta = new Vector2(rt.sizeDelta.x, listStep);
                FitText(listRows[i], scales[i]);
            }

            // 키보드 이동 대상을 목록 버튼으로 교체
            var rowSels = listRows.Select(r => r.GetComponent<MenualSelectionUI>()).Where(s => s != null).ToList();
            ActiveSelectionsProp.SetValue(title, rowSels);
            if (keyboard && rowSels.Count > 0) rowSels[0].Enter();
        }

        private static void DestroyRows()
        {
            foreach (var row in listRows)
                if (row != null)
                {
                    row.gameObject.SetActive(false); // Destroy 는 프레임 끝에 처리되므로 먼저 꺼서 바로 안 보이게
                    UnityEngine.Object.Destroy(row.gameObject);
                }
            listRows.Clear();
        }

        public static void CloseList()
        {
            if (listRows.Count == 0 && hiddenOriginals.Count == 0) return;

            bool keyboard = MenualSelectionUI.IsMenual;
            MenualSelectionUI.Clear();

            DestroyRows();
            foreach (var go in hiddenOriginals)
                if (go != null) go.SetActive(true);
            hiddenOriginals.Clear();

            if (title != null && savedActive != null)
            {
                ActiveSelectionsProp.SetValue(title, savedActive);
                savedActive = null;
                if (keyboard && menuButton != null)
                    menuButton.GetComponent<MenualSelectionUI>()?.Enter();
            }
        }

        private static string Label(string name, bool on) =>
            on ? $"{name} : ON" : $"{name} : <color=#888888>OFF</color>";

        /// <summary>
        /// 글자 크기를 원래의 scale 배로 맞추고, 이름이 길면 줄바꿈 없이 더 줄여서 한 줄에 맞춤 (최소 원래 크기의 50%)
        /// </summary>
        private static void FitText(ButtonUI button, float scale)
        {
            foreach (var tmp in button.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                float size = tmp.fontSize;
                tmp.enableWordWrapping = false;
                tmp.fontSizeMax = size * scale;
                tmp.fontSizeMin = size * 0.5f;
                tmp.enableAutoSizing = true;
            }
        }

        private static void SetText(ButtonUI button, string text)
        {
            var tui = button.GetComponentInChildren<TextUI>(true);
            if (tui != null) tui.Play(() => text);
        }

        private const float ListStepRatio = 0.75f;   // 목록 줄 간격 = 원래 메뉴 간격 x 0.75
        private const float ModTextScale = 0.85f;    // 모드 줄 글자 크기
        private const float OptionTextScale = 0.7f;  // 세부 옵션 줄 글자 크기 (모드 줄보다 작게 해서 소속이 보이도록)
        private static float listStep;

        /// <summary>목록의 한 줄: 모드 ON/OFF 줄 또는 그 모드의 세부 옵션 줄</summary>
        private class Row
        {
            public Func<string> Label;
            public Action Toggle;
            public bool IsOption;
        }

        private class ModGroup
        {
            public string Display;
            public string Category;
            public List<Row> Rows = new List<Row>();
        }

        /// <summary>
        /// 줄들을 페이지로 나눔. 모드 하나와 그 세부 옵션은 같은 페이지에 오도록 모드 단위로 채우고,
        /// 모드 하나가 한 페이지보다 길 때만 중간에서 나눔.
        /// 전부 한 페이지에 들어가면 "돌아가기" 1칸만, 아니면 "다음 페이지" + "돌아가기" 2칸을 남김.
        /// </summary>
        private static List<List<Row>> BuildPages(List<ModGroup> groups)
        {
            int total = groups.Sum(g => g.Rows.Count);
            int capacity = total + 1 <= listSlots ? Math.Max(total, 1) : Math.Max(listSlots - 2, 1);
            var pages = new List<List<Row>>();
            var current = new List<Row>();
            foreach (var group in groups)
            {
                if (current.Count > 0 && current.Count + group.Rows.Count > capacity)
                {
                    pages.Add(current);
                    current = new List<Row>();
                }
                foreach (var row in group.Rows)
                {
                    if (current.Count >= capacity)
                    {
                        pages.Add(current);
                        current = new List<Row>();
                    }
                    current.Add(row);
                }
            }
            pages.Add(current);
            return pages;
        }

        /// <summary>
        /// 모드마다 ON/OFF 줄 1개 + 세부 옵션 줄 n개.
        /// 세부 옵션은 각 모드 Plugin 클래스의
        ///   public const string MenuOptions = "섹션/키;섹션/키"   (보여줄 설정 항목)
        ///   public static string MenuText(string key, string value, bool korean)   (항목 이름/값 표시 문구, value 가 null 이면 항목 이름)
        /// 를 리플렉션으로 읽는다. bool 은 ON/OFF, 선택지 목록(AcceptableValueList)이나 enum 은 누를 때마다 다음 값으로 바뀜.
        /// </summary>
        private static List<ModGroup> CollectGroups()
        {
            var groups = new List<ModGroup>();
            var enabledKey = new ConfigDefinition("General", "Enabled");
            foreach (var info in Chainloader.PluginInfos.Values)
            {
                if (info.Instance == null || info.Metadata.GUID == Plugin.Guid) continue;
                var config = info.Instance.Config;
                if (!config.TryGetEntry<bool>(enabledKey, out var enabled)) continue;

                // 각 모드 Plugin 클래스의 public const 표시 정보 (다른 사람이 만든 모드처럼 없으면 기본값)
                var type = info.Instance.GetType();
                string category = ReadConst(type, "ModCategory", "");
                string nameKo = ReadConst(type, "NameKo", "");
                bool singleOnly = ReadConst(type, "SingleOnly", false);

                string name = Text.IsKorean && nameKo != "" ? nameKo : info.Metadata.Name;
                if (category != "") name = category + "_" + name;
                if (singleOnly) name += " " + Text.SingleOnly;

                var group = new ModGroup { Display = name, Category = category };
                string display = name;
                group.Rows.Add(new Row
                {
                    Label = () => Label(display, enabled.Value),
                    Toggle = () => enabled.Value = !enabled.Value
                });

                var menuText = type.GetMethod("MenuText", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(string), typeof(string), typeof(bool) }, null);
                foreach (var spec in ReadConst(type, "MenuOptions", "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int slash = spec.IndexOf('/');
                    if (slash <= 0) continue;
                    var def = new ConfigDefinition(spec.Substring(0, slash).Trim(), spec.Substring(slash + 1).Trim());
                    // ConfigFile 은 IDictionary 를 "명시적으로" 구현해서 인터페이스로 바꿔야 TryGetValue 가 보임
                    if (!((IDictionary<ConfigDefinition, ConfigEntryBase>)config).TryGetValue(def, out var option)) continue;

                    string key = def.Key;
                    Func<string, string> textOf = value =>
                    {
                        try { return menuText?.Invoke(null, new object[] { key, value, Text.IsKorean }) as string; }
                        catch { return null; }
                    };
                    group.Rows.Add(new Row
                    {
                        Label = () => "  - " + (textOf(null) ?? key) + " : " + OptionValueText(option, textOf),
                        Toggle = () => CycleOption(option),
                        IsOption = true
                    });
                }
                groups.Add(group);
            }
            // UX → UI → BAL → 기타 순, 같은 분류 안에서는 이름순. 세부 옵션 줄은 자기 모드 바로 아래에 붙어 다님
            groups.Sort((a, b) =>
            {
                int c = CategoryOrder(a.Category).CompareTo(CategoryOrder(b.Category));
                return c != 0 ? c : string.CompareOrdinal(a.Display, b.Display);
            });
            return groups;
        }

        private static string OptionValueText(ConfigEntryBase option, Func<string, string> textOf)
        {
            object value = option.BoxedValue;
            if (value is bool on) return on ? "ON" : "<color=#888888>OFF</color>";
            string raw = value?.ToString() ?? "";
            return textOf(raw) ?? raw;
        }

        /// <summary>bool 은 반전, 선택지 목록/enum 은 다음 값으로 (마지막 다음은 처음)</summary>
        private static void CycleOption(ConfigEntryBase option)
        {
            object value = option.BoxedValue;
            if (value is bool on) { option.BoxedValue = !on; return; }

            object[] values = null;
            if (option.Description?.AcceptableValues is AcceptableValueList<string> list) values = list.AcceptableValues;
            else if (option.SettingType.IsEnum) values = Enum.GetValues(option.SettingType).Cast<object>().ToArray();
            if (values == null || values.Length == 0) return;

            int index = Array.IndexOf(values, value);
            option.BoxedValue = values[(index + 1) % values.Length];
        }

        private static int CategoryOrder(string category) =>
            category == "UX" ? 0 : category == "UI" ? 1 : category == "BAL" ? 2 : 3;

        /// <summary>const 필드는 리플렉션에서 static 필드로 보이므로 GetValue(null) 로 읽을 수 있음</summary>
        private static T ReadConst<T>(Type type, string field, T fallback)
        {
            var f = type.GetField(field, BindingFlags.Public | BindingFlags.Static);
            return f != null && f.GetValue(null) is T value ? value : fallback;
        }
    }

    /// <summary>표시 문구 (게임 언어가 한국어면 한국어, 그 외 언어는 영어)</summary>
    internal static class Text
    {
        public static bool IsKorean
        {
            get
            {
                // 설정에서 직접 고른 언어가 있으면 그것을 우선 (게임은 영어로 하면서 모드 메뉴만 한국어로 보고 싶은 경우 등)
                string forced = Plugin.Language?.Value;
                if (forced == "Korean") return true;
                if (forced == "English") return false;
                try
                {
                    string key = Option.Instance?.languageKey;
                    if (string.IsNullOrEmpty(key)) key = Option.GetLanguageKey(Application.systemLanguage); // 게임과 같은 기본값 규칙
                    return key == "KOR";
                }
                catch { return Application.systemLanguage == SystemLanguage.Korean; }
            }
        }

        public static string MenuTitle => IsKorean ? "모드 관리" : "Mods";
        public static string Back => IsKorean ? "돌아가기" : "Back";
        public static string SingleOnly => IsKorean ? "[싱글 전용]" : "[Single only]";
        /// <summary>마지막 페이지에서는 첫 페이지로 돌아가므로 문구도 그에 맞춤</summary>
        public static string NextPage(int cur, int total) => cur < total
            ? (IsKorean ? $"다음 페이지 ({cur}/{total})" : $"Next page ({cur}/{total})")
            : (IsKorean ? $"첫 페이지로 ({cur}/{total})" : $"First page ({cur}/{total})");
    }
}
