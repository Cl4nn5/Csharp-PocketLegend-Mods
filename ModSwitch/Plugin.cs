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
    /// 타이틀 메뉴의 "도전 기록"과 "크레딧" 사이에 "모드 관리" 메뉴를 추가한다.
    /// 설정에 [General] Enabled (bool) 항목을 가진 플러그인이 자동으로 목록에 나타난다.
    /// </summary>
    [BepInPlugin(Guid, "Mod Switch", "0.3.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "pocketlegend.modswitch";
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            new Harmony(Guid).PatchAll(typeof(Patches));
        }

        // 타이틀 화면이 플러그인 로드보다 먼저 떠 있는 경우를 대비해 매 프레임 확인 (주입 후엔 비용 거의 없음)
        private void Update()
        {
            var title = Getable.Get<TitleUI>();
            if (title != null && title.gameObject.activeInHierarchy)
                ModMenu.Ensure(title);
        }
    }

    internal static class Patches
    {
        [HarmonyPostfix, HarmonyPatch(typeof(TitleUI), nameof(TitleUI.Show))]
        private static void TitleUI_Show(TitleUI __instance)
        {
            ModMenu.CloseList(); // 게임에서 타이틀로 돌아왔을 때는 기본 메뉴 상태로
            ModMenu.Ensure(__instance);
            ModMenu.SyncActiveSelections(__instance);
        }
    }

    internal static class ModMenu
    {
        private static readonly string[] ButtonFields =
            { "continueButton", "newGameButton", "recordButton", "creditButton", "exitButton" };

        private static readonly FieldInfo MenualSelectionsField = AccessTools.Field(typeof(TitleUI), "menualSelections");
        private static readonly PropertyInfo ActiveSelectionsProp = AccessTools.Property(typeof(TitleUI), "ActiveMenualSelections");
        private static readonly MethodInfo OnStateMethod = AccessTools.Method(typeof(TitleUI), "OnState");

        private static TitleUI title;
        private static ButtonUI menuButton;
        private static List<Vector3> slots = new List<Vector3>(); // 메뉴 6칸의 위치 (위→아래)

        // 목록 모드 상태
        private static readonly List<GameObject> hiddenOriginals = new List<GameObject>();
        private static readonly List<ButtonUI> listRows = new List<ButtonUI>();
        private static List<MenualSelectionUI> savedActive;

        private static ButtonUI GetButton(TitleUI t, string field) =>
            (ButtonUI)AccessTools.Field(typeof(TitleUI), field).GetValue(t);

        private static List<MenualSelectionUI> MenualSelections(TitleUI t) =>
            (List<MenualSelectionUI>)MenualSelectionsField.GetValue(t);

        private static List<MenualSelectionUI> ActiveSelections(TitleUI t) =>
            (List<MenualSelectionUI>)ActiveSelectionsProp.GetValue(t);

        public static void Ensure(TitleUI t)
        {
            if (menuButton != null && title == t) return;
            try
            {
                title = t;
                var buttons = ButtonFields.Select(f => GetButton(t, f)).ToList();
                if (buttons.Any(b => b == null)) { Plugin.Log.LogWarning("타이틀 메뉴 버튼을 찾지 못함"); return; }
                var record = buttons[2];
                var credit = buttons[3];

                // 1) 크레딧 버튼을 복제해서 "모드 관리" 생성
                menuButton = CloneTitleButton(credit, "모드 관리", OpenList);
                menuButton.gameObject.name = "ModSwitch_MenuButton";

                // 2) 기존 5칸(맨 위~맨 아래) 범위 안에 6칸을 같은 간격으로 재배치
                Vector3 top = buttons[0].transform.localPosition;
                Vector3 bottom = buttons[4].transform.localPosition;
                slots = Enumerable.Range(0, 6).Select(i => Vector3.Lerp(top, bottom, i / 5f)).ToList();
                var ordered = new List<ButtonUI> { buttons[0], buttons[1], record, menuButton, credit, buttons[4] };
                for (int i = 0; i < ordered.Count; i++)
                    ordered[i].transform.localPosition = slots[i];

                // 3) 키보드 이동 목록에 "도전 기록" 바로 다음으로 등록
                var menuSel = menuButton.GetComponent<MenualSelectionUI>();
                if (menuSel != null)
                {
                    var list = MenualSelections(t);
                    var recordSel = record.GetComponent<MenualSelectionUI>();
                    int idx = recordSel != null ? list.IndexOf(recordSel) : -1;
                    list.Insert(idx >= 0 ? idx + 1 : list.Count, menuSel);
                    SyncActiveSelections(t);
                }

                Plugin.Log.LogInfo($"'모드 관리' 메뉴 추가됨 (간격 {Vector3.Distance(slots[0], slots[1]):0.#})");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }

        /// <summary>Show() 이후에 메뉴를 넣었을 때 키보드 이동 목록을 다시 계산</summary>
        public static void SyncActiveSelections(TitleUI t)
        {
            if (ActiveSelections(t) == null) return;
            var active = MenualSelections(t)
                .Where(x => x != null && !x.GetComponent<ButtonUI>().IsDisable)
                .ToList();
            ActiveSelectionsProp.SetValue(t, active);
        }

        /// <summary>
        /// 크레딧 버튼을 복제해서 폰트/외곽선/호버/키보드 커서가 동일한 버튼을 만든다.
        /// </summary>
        private static ButtonUI CloneTitleButton(ButtonUI source, string text, Action onClick)
        {
            var go = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent, false);

            // TextUISetter: Start 에서 원래 문구("크레딧")로 덮어쓰므로 제거
            foreach (var setter in go.GetComponentsInChildren<TextUISetter>(true))
                UnityEngine.Object.DestroyImmediate(setter);

            // 복제본은 원본과 같은 재질 인스턴스를 참조하므로, 호버 시 외곽선 색 변경이 원본(크레딧)에도 번진다 → 재질 분리
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

        private static void OpenList()
        {
            if (title == null || menuButton == null) return;
            CloseList();

            bool keyboard = MenualSelectionUI.IsMenual;
            MenualSelectionUI.Clear();

            // 원래 메뉴 버튼들을 잠시 숨김
            var container = menuButton.transform.parent;
            for (int i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i).gameObject;
                if (child.activeSelf && child.GetComponent<ButtonUI>() != null)
                {
                    hiddenOriginals.Add(child);
                    child.SetActive(false);
                }
            }

            var credit = GetButton(title, "creditButton");
            foreach (var (name, entry) in CollectEntries())
            {
                ButtonUI row = null;
                row = CloneTitleButton(credit, Label(name, entry.Value), () =>
                {
                    entry.Value = !entry.Value; // 설정 파일에 즉시 저장됨
                    Plugin.Log.LogInfo($"{name} => {(entry.Value ? "ON" : "OFF")}");
                    SetText(row, Label(name, entry.Value));
                });
                listRows.Add(row);
            }
            listRows.Add(CloneTitleButton(credit, "돌아가기", CloseList));

            // 메뉴 칸 위치에 순서대로 배치 (칸이 모자라면 같은 간격으로 아래로 이어감)
            Vector3 step = slots.Count > 1 ? slots[1] - slots[0] : new Vector3(0, -80, 0);
            for (int i = 0; i < listRows.Count; i++)
                listRows[i].transform.localPosition = i < slots.Count ? slots[i] : slots[slots.Count - 1] + step * (i - slots.Count + 1);

            // 키보드 이동 대상을 목록 버튼으로 교체
            savedActive = ActiveSelections(title);
            var rowSels = listRows.Select(r => r.GetComponent<MenualSelectionUI>()).Where(s => s != null).ToList();
            ActiveSelectionsProp.SetValue(title, rowSels);
            if (keyboard && rowSels.Count > 0) rowSels[0].Enter();
        }

        public static void CloseList()
        {
            if (listRows.Count == 0 && hiddenOriginals.Count == 0) return;

            bool keyboard = MenualSelectionUI.IsMenual;
            MenualSelectionUI.Clear();

            foreach (var row in listRows)
                if (row != null) UnityEngine.Object.Destroy(row.gameObject);
            listRows.Clear();
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

        private static void SetText(ButtonUI button, string text)
        {
            var tui = button.GetComponentInChildren<TextUI>(true);
            if (tui != null) tui.Play(() => text);
        }

        private static List<(string name, ConfigEntry<bool> entry)> CollectEntries()
        {
            var list = new List<(string, ConfigEntry<bool>)>();
            var key = new ConfigDefinition("General", "Enabled");
            foreach (var info in Chainloader.PluginInfos.Values)
            {
                if (info.Instance == null || info.Metadata.GUID == Plugin.Guid) continue;
                if (info.Instance.Config.TryGetEntry<bool>(key, out var entry))
                    list.Add((info.Metadata.Name, entry));
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
            return list;
        }
    }
}
