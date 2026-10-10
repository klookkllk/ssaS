using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WeaponCustomization
{
    /// <summary>
    /// Меню обвеса оружия. Открывается/закрывается клавишей M (Esc тоже закрывает).
    /// Слева — 3D-превью текущего оружия (можно крутить мышью), справа — выбор обвеса.
    /// UI строится кодом, поэтому достаточно повесить компонент на любой объект сцены.
    /// </summary>
    public class WeaponCustomizationMenu : MonoBehaviour
    {
        [Tooltip("Оружие с WeaponAttachmentSystem. Если пусто — берётся первое активное в сцене " +
                 "(т.е. то, что сейчас в руках).")]
        public WeaponAttachmentSystem weapon;

        [Tooltip("Компоненты, которые выключаются, пока меню открыто: контроллер игрока KINEMATION, " +
                 "PlayerInput, скрипт обзора мышью и т.п. — чтобы не стрелять и не крутить камеру.")]
        public Behaviour[] disableWhileOpen;

        [Tooltip("Разблокировать и показать курсор, пока меню открыто.")]
        public bool manageCursor = true;

        [Tooltip("Ставить игру на паузу (Time.timeScale = 0), пока меню открыто.")]
        public bool pauseGame = false;

        [Tooltip("Слой, на котором рисуется 3D-превью. Должен быть свободным (по умолчанию 31).")]
        [Range(0, 31)] public int previewLayer = 31;

        public bool IsOpen { get; private set; }

        private static readonly Color PanelColor = new Color(0.06f, 0.07f, 0.08f, 0.92f);
        private static readonly Color ButtonColor = new Color(0.16f, 0.18f, 0.2f, 1f);
        private static readonly Color SelectedColor = new Color(0.85f, 0.45f, 0.1f, 1f);

        private GameObject _canvasRoot;
        private Text _titleText;
        private RectTransform _optionsRoot;
        private WeaponPreviewRenderer _preview;
        private Font _font;
        private WeaponAttachmentSystem _shownWeapon;
        private readonly List<(Button button, int slot, int option)> _optionButtons = new List<(Button, int, int)>();

        private CursorLockMode _prevLock;
        private bool _prevCursorVisible;
        private float _prevTimeScale = 1f;
        private readonly List<Behaviour> _disabledByMenu = new List<Behaviour>();

        [Tooltip("Часть имени объекта оружия, по которой оно ищется автоматически, " +
                 "если на нём нет WeaponAttachmentSystem.")]
        public string autoWeaponNameContains = "MX16";

        // Меню создаётся само при запуске игры — ничего не нужно добавлять в сцену вручную.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAll<WeaponCustomizationMenu>().Length > 0) return;
            var go = new GameObject("WeaponCustomizationMenu (auto)");
            DontDestroyOnLoad(go);
            go.AddComponent<WeaponCustomizationMenu>();
        }

        private void Start()
        {
            Debug.Log("[WeaponCustomization] Меню обвеса готово — нажмите M.");
            _font = LoadFont();
            var previewGo = new GameObject("WeaponPreviewRig");
            _preview = previewGo.AddComponent<WeaponPreviewRenderer>();
            _preview.Init(previewLayer, 1024, 640);
            _preview.SetVisible(false);
            BuildUI();
            _canvasRoot.SetActive(false);
        }

        private void Update()
        {
            if (TogglePressed()) SetOpen(!IsOpen);
            else if (IsOpen && EscapePressed()) SetOpen(false);
            else if (IsOpen) HandleNudgeKeys();
        }

        private void LateUpdate()
        {
            // Контроллеры FPS часто блокируют курсор каждый кадр — держим его свободным, пока меню открыто.
            if (IsOpen && manageCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        public void SetOpen(bool open)
        {
            if (open == IsOpen) return;

            if (open)
            {
                WeaponAttachmentSystem target = ResolveWeapon();
                IsOpen = true;
                EnsureEventSystem();

                _disabledByMenu.Clear();
                IEnumerable<Behaviour> toDisable = disableWhileOpen != null && disableWhileOpen.Length > 0
                    ? disableWhileOpen
                    : FindPlayerControllers();
                foreach (Behaviour b in toDisable)
                {
                    if (b != null && b.enabled) { b.enabled = false; _disabledByMenu.Add(b); }
                }

                if (manageCursor)
                {
                    _prevLock = Cursor.lockState;
                    _prevCursorVisible = Cursor.visible;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }

                if (pauseGame)
                {
                    _prevTimeScale = Time.timeScale;
                    Time.timeScale = 0f;
                }

                ShowWeapon(target);
                _canvasRoot.SetActive(true);
                _preview.SetVisible(true);
                Debug.Log("[WeaponCustomization] Меню открыто. Оружие: " +
                          (target != null ? target.name : "не найдено"));
            }
            else
            {
                IsOpen = false;
                _canvasRoot.SetActive(false);
                _preview.SetVisible(false);
                ShowWeapon(null);

                foreach (Behaviour b in _disabledByMenu) if (b != null) b.enabled = true;
                _disabledByMenu.Clear();

                if (manageCursor)
                {
                    Cursor.lockState = _prevLock;
                    Cursor.visible = _prevCursorVisible;
                }

                if (pauseGame) Time.timeScale = _prevTimeScale;
            }
        }

        private WeaponAttachmentSystem ResolveWeapon()
        {
            if (weapon != null && weapon.isActiveAndEnabled) return weapon;
            foreach (WeaponAttachmentSystem w in FindAll<WeaponAttachmentSystem>())
            {
                if (w.isActiveAndEnabled) return w;
            }

            // Ничего не настроено вручную — ищем оружие в руках по имени и настраиваем автоматически.
            if (!string.IsNullOrEmpty(autoWeaponNameContains))
            {
                string key = autoWeaponNameContains.ToLowerInvariant();
                foreach (Transform t in FindAll<Transform>())
                {
                    if (!t.gameObject.activeInHierarchy || !t.name.ToLowerInvariant().Contains(key)) continue;
                    // Берём самый верхний объект с таким именем.
                    if (t.parent != null && t.parent.name.ToLowerInvariant().Contains(key)) continue;
                    if (t.GetComponentInChildren<Renderer>() == null) continue;
                    return WeaponAttachmentSystem.AutoSetup(t.gameObject);
                }
                Debug.LogWarning("[WeaponCustomization] Не найдено активное оружие с именем, содержащим '" +
                                 autoWeaponNameContains + "'. Возьмите MX16A4 в руки или назначьте поле Weapon.");
            }
            return weapon;
        }

        // Контроллер игрока KINEMATION и PlayerInput — чтобы при открытом меню не стрелять и не крутить камеру.
        private static IEnumerable<Behaviour> FindPlayerControllers()
        {
            foreach (Behaviour b in FindAll<Behaviour>())
            {
                string n = b.GetType().Name;
                if (n == "PlayerInput" || n.Contains("FPSController") || n.Contains("FPSPlayer"))
                    yield return b;
            }
        }

        private static T[] FindAll<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return FindObjectsByType<T>(FindObjectsSortMode.None);
#else
            return FindObjectsOfType<T>();
#endif
        }

        private void ShowWeapon(WeaponAttachmentSystem target)
        {
            if (_shownWeapon != null) _shownWeapon.Changed -= OnWeaponChanged;
            _shownWeapon = target;
            if (_shownWeapon != null) _shownWeapon.Changed += OnWeaponChanged;

            RebuildOptions();
            _preview.Show(_shownWeapon != null ? _shownWeapon.gameObject : null);
        }

        private void OnWeaponChanged()
        {
            RefreshSelection();
            _preview.Show(_shownWeapon.gameObject);
        }

        // ---------------------------------------------------------------- UI

        private void BuildUI()
        {
            _canvasRoot = new GameObject("WeaponCustomizationCanvas");
            _canvasRoot.transform.SetParent(transform, false);
            var canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = _canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasRoot.AddComponent<GraphicRaycaster>();

            // Затемнение фона
            RectTransform dim = CreateRect("Dim", _canvasRoot.transform);
            Stretch(dim);
            dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            // Главное окно
            RectTransform window = CreateRect("Window", _canvasRoot.transform);
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.sizeDelta = new Vector2(1600, 820);
            window.gameObject.AddComponent<Image>().color = PanelColor;

            _titleText = CreateText("Title", window, "", 44, TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(_titleText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(40, -90), new Vector2(-40, -20));

            Text hint = CreateText("Hint", window, "M / Esc — закрыть   •   ЛКМ + мышь — вращать оружие", 22,
                TextAnchor.MiddleRight, FontStyle.Normal);
            hint.color = new Color(1f, 1f, 1f, 0.5f);
            SetRect(hint.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(40, -90), new Vector2(-40, -20));

            // 3D-превью
            RectTransform previewFrame = CreateRect("PreviewFrame", window);
            SetRect(previewFrame, new Vector2(0, 0), new Vector2(0, 1), new Vector2(40, 40), new Vector2(1080, -110));
            previewFrame.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);

            RectTransform previewImageRect = CreateRect("Preview3D", previewFrame);
            Stretch(previewImageRect);
            var raw = previewImageRect.gameObject.AddComponent<RawImage>();
            raw.texture = _preview.Texture;
            previewImageRect.gameObject.AddComponent<PreviewDragHandler>().preview = _preview;

            // Панель обвеса
            RectTransform side = CreateRect("Attachments", window);
            SetRect(side, new Vector2(0, 0), new Vector2(1, 1), new Vector2(1120, 40), new Vector2(-40, -110));
            side.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);
            var layout = side.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.spacing = 12;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            _optionsRoot = side;
        }

        private void RebuildOptions()
        {
            foreach (Transform child in _optionsRoot) Destroy(child.gameObject);
            _optionButtons.Clear();

            if (_shownWeapon == null)
            {
                _titleText.text = "Нет оружия";
                AddLabel("В руках нет оружия с WeaponAttachmentSystem.", 24, FontStyle.Normal);
                return;
            }

            _titleText.text = _shownWeapon.weaponName;

            for (int s = 0; s < _shownWeapon.slots.Count; s++)
            {
                AttachmentSlot slot = _shownWeapon.slots[s];
                AddLabel(slot.slotName.ToUpperInvariant(), 26, FontStyle.Bold);

                AddOptionButton("Без обвеса", s, -1);
                for (int o = 0; o < slot.options.Count; o++)
                {
                    AddOptionButton(slot.options[o].displayName, s, o);
                }

                AddLabel("Положение (стрелки на клавиатуре, Shift — точнее)", 20, FontStyle.Normal);
                int slotIndex = s;
                AddNudgeRow(("Выше", () => Nudge(slotIndex, Vector3.up)),
                            ("Ниже", () => Nudge(slotIndex, Vector3.down)));
                AddNudgeRow(("Вперёд", () => Nudge(slotIndex, Vector3.forward)),
                            ("Назад", () => Nudge(slotIndex, Vector3.back)));
                AddNudgeRow(("Влево", () => Nudge(slotIndex, Vector3.left)),
                            ("Вправо", () => Nudge(slotIndex, Vector3.right)),
                            ("Сброс", () => _shownWeapon.Nudge(slotIndex, null)));
            }

            RefreshSelection();
        }

        private const float NudgeStep = 0.005f;     // 5 мм
        private const float NudgeStepFine = 0.001f; // 1 мм

        private void Nudge(int slot, Vector3 dir)
        {
            if (_shownWeapon == null) return;
            _shownWeapon.Nudge(slot, dir * (ShiftHeld() ? NudgeStepFine : NudgeStep));
        }

        private void AddNudgeRow(params (string label, UnityEngine.Events.UnityAction action)[] buttons)
        {
            RectTransform row = CreateRect("NudgeRow", _optionsRoot);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 48;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;

            foreach ((string label, UnityEngine.Events.UnityAction action) in buttons)
            {
                RectTransform rect = CreateRect("Nudge_" + label, row);
                var image = rect.gameObject.AddComponent<Image>();
                image.color = ButtonColor;
                var button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                button.onClick.AddListener(action);
                Text text = CreateText("Text", rect, label, 22, TextAnchor.MiddleCenter, FontStyle.Normal);
                SetRect(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            }
        }

        private void HandleNudgeKeys()
        {
            if (_shownWeapon == null) return;
            for (int s = 0; s < _shownWeapon.slots.Count; s++)
            {
                if (_shownWeapon.slots[s].currentInstance == null) continue;
                // Стрелки: вверх/вниз — высота, влево/вправо — назад/вперёд вдоль ствола.
                if (KeyDown(0)) Nudge(s, Vector3.up);
                if (KeyDown(1)) Nudge(s, Vector3.down);
                if (KeyDown(2)) Nudge(s, Vector3.back);
                if (KeyDown(3)) Nudge(s, Vector3.forward);
                return; // двигаем первый установленный обвес
            }
        }

        // 0 — вверх, 1 — вниз, 2 — влево, 3 — вправо
        private static bool KeyDown(int arrow)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return false;
            switch (arrow)
            {
                case 0: return kb.upArrowKey.wasPressedThisFrame;
                case 1: return kb.downArrowKey.wasPressedThisFrame;
                case 2: return kb.leftArrowKey.wasPressedThisFrame;
                default: return kb.rightArrowKey.wasPressedThisFrame;
            }
#else
            switch (arrow)
            {
                case 0: return Input.GetKeyDown(KeyCode.UpArrow);
                case 1: return Input.GetKeyDown(KeyCode.DownArrow);
                case 2: return Input.GetKeyDown(KeyCode.LeftArrow);
                default: return Input.GetKeyDown(KeyCode.RightArrow);
            }
#endif
        }

        private static bool ShiftHeld()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.shiftKey.isPressed;
#else
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
        }

        private void AddLabel(string text, int size, FontStyle style)
        {
            Text label = CreateText("Label", _optionsRoot, text, size, TextAnchor.MiddleLeft, style);
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = size + 20;
        }

        private void AddOptionButton(string label, int slot, int option)
        {
            RectTransform rect = CreateRect("Option_" + label, _optionsRoot);
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Color.white;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            Text text = CreateText("Text", rect, label, 26, TextAnchor.MiddleLeft, FontStyle.Normal);
            SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(20, 0), new Vector2(-20, 0));

            button.onClick.AddListener(() =>
            {
                if (_shownWeapon != null) _shownWeapon.Equip(slot, option);
            });
            _optionButtons.Add((button, slot, option));
        }

        private void RefreshSelection()
        {
            if (_shownWeapon == null) return;
            foreach ((Button button, int slot, int option) in _optionButtons)
            {
                bool selected = _shownWeapon.GetCurrentOption(slot) == option;
                ColorBlock colors = button.colors;
                Color baseColor = selected ? SelectedColor : ButtonColor;
                colors.normalColor = baseColor;
                colors.highlightedColor = baseColor * 1.25f;
                colors.pressedColor = baseColor * 0.8f;
                colors.selectedColor = baseColor;
                colors.colorMultiplier = 1f;
                button.colors = colors;
                button.targetGraphic.color = Color.white;
            }
        }

        // ---------------------------------------------------------- helpers

        private RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private Text CreateText(string name, Transform parent, string value, int size, TextAnchor anchor, FontStyle style)
        {
            RectTransform rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = _font;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            SetRect(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static Font LoadFont()
        {
            // Unity 2022.2+ переименовала встроенный шрифт.
            Font font = null;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (font == null)
            {
                try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            }
            return font;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAll<EventSystem>().Length > 0) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        private static bool TogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.mKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.M);
#endif
        }

        private static bool EscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        private void OnDisable()
        {
            if (IsOpen) SetOpen(false);
        }
    }
}
