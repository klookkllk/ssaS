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

        [Header("Стиль (как у HUD)")]
        [Tooltip("Акцентный цвет линий и выделения — под цвет перекрестия HUD.")]
        public Color accentColor = new Color(0.55f, 0.88f, 0.98f, 1f);

        [Tooltip("Шрифт HUD. Если пусто — встроенный шрифт Unity.")]
        public Font customFont;

        private GameObject _canvasRoot;
        private Text _titleText;
        private RectTransform _optionsRoot;
        private WeaponPreviewRenderer _preview;
        private Font _font;
        private WeaponAttachmentSystem _shownWeapon;

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
            _font = customFont != null ? customFont : LoadFont();
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
            if (IsOpen) AnimateFade();
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
                _fade = 0f;
                _canvasGroup.alpha = 0f;
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
        // Стиль — как у HUD KINEMATION: тонкие циановые линии (как у прицела-перекрестия),
        // уголки-скобки, полупрозрачный тёмный фон, заглавные буквы.

        private class OptionCard
        {
            public Button button;
            public Image fill;
            public Image[] border;
            public Image bar;
            public Text label;
            public Text status;
            public int slot;
            public int option;
        }

        private readonly List<OptionCard> _cards = new List<OptionCard>();
        private CanvasGroup _canvasGroup;
        private float _fade;

        private Color Accent => accentColor;
        private Color AccentA(float a) => new Color(accentColor.r, accentColor.g, accentColor.b, a);
        private static readonly Color TextDim = new Color(1f, 1f, 1f, 0.55f);

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
            _canvasGroup = _canvasRoot.AddComponent<CanvasGroup>();

            // Затемнение с виньеткой — сцена остаётся видна, как под HUD.
            RectTransform dim = CreateRect("Dim", _canvasRoot.transform);
            Stretch(dim);
            dim.gameObject.AddComponent<Image>().color = new Color(0.01f, 0.015f, 0.02f, 0.45f);
            RectTransform vignette = CreateRect("Vignette", _canvasRoot.transform);
            Stretch(vignette);
            var vig = vignette.gameObject.AddComponent<RawImage>();
            vig.texture = BuildVignette();
            vig.color = new Color(0f, 0f, 0f, 0.85f);
            vig.raycastTarget = false;

            // ── Заголовок
            Text tag = CreateText("Tag", _canvasRoot.transform, "// ОБВЕС ОРУЖИЯ", 20, TextAnchor.LowerLeft, FontStyle.Bold);
            tag.color = Accent;
            SetRect(tag.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(80, -100), new Vector2(700, -70));

            _titleText = CreateText("Title", _canvasRoot.transform, "", 60, TextAnchor.UpperLeft, FontStyle.Bold);
            SetRect(_titleText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(76, -180), new Vector2(1000, -100));

            Line(_canvasRoot.transform, new Vector2(0, 1), new Vector2(80, -186), new Vector2(420, 2), Accent);
            Line(_canvasRoot.transform, new Vector2(0, 1), new Vector2(504, -186), new Vector2(16, 2), AccentA(0.5f));
            Line(_canvasRoot.transform, new Vector2(0, 1), new Vector2(524, -186), new Vector2(8, 2), AccentA(0.3f));

            // ── 3D-превью с уголками
            RectTransform previewFrame = CreateRect("PreviewFrame", _canvasRoot.transform);
            SetRect(previewFrame, new Vector2(0, 0), new Vector2(0, 1), new Vector2(80, 150), new Vector2(1180, -230));
            var bg = previewFrame.gameObject.AddComponent<Image>();
            bg.color = AccentA(0.03f);
            Frame(previewFrame, AccentA(0.15f), 1f);
            Brackets(previewFrame, Accent, 34f, 2f);

            RectTransform previewImageRect = CreateRect("Preview3D", previewFrame);
            Stretch(previewImageRect);
            var raw = previewImageRect.gameObject.AddComponent<RawImage>();
            raw.texture = _preview.Texture;
            previewImageRect.gameObject.AddComponent<PreviewDragHandler>().preview = _preview;

            Text previewCaption = CreateText("Caption", previewFrame, "3D-ПРЕВЬЮ", 16, TextAnchor.UpperLeft, FontStyle.Bold);
            previewCaption.color = AccentA(0.7f);
            SetRect(previewCaption.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -40), new Vector2(-22, -14));

            // ── Правая колонка — выбор обвеса
            RectTransform side = CreateRect("Attachments", _canvasRoot.transform);
            SetRect(side, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-660, 150), new Vector2(-80, -230));
            var layout = side.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            _optionsRoot = side;

            // ── Подсказки клавиш внизу
            Text hints = CreateText("Hints", _canvasRoot.transform,
                "[M] ЗАКРЫТЬ     [ЛКМ] ВРАЩАТЬ     [↑ ↓ ← →] ПОЛОЖЕНИЕ     [SHIFT] ТОЧНЕЕ", 18,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            hints.color = TextDim;
            SetRect(hints.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(80, 70), new Vector2(-80, 110));
            Line(_canvasRoot.transform, new Vector2(0, 0), new Vector2(80, 124), new Vector2(1760, 1), AccentA(0.25f), new Vector2(0, 0));
        }

        private void AnimateFade()
        {
            if (_canvasGroup == null) return;
            _fade = Mathf.MoveTowards(_fade, 1f, Time.unscaledDeltaTime / 0.15f);
            _canvasGroup.alpha = _fade;
        }

        private void RebuildOptions()
        {
            foreach (Transform child in _optionsRoot) Destroy(child.gameObject);
            _cards.Clear();

            if (_shownWeapon == null)
            {
                _titleText.text = "НЕТ ОРУЖИЯ";
                AddLabel("В руках нет оружия с WeaponAttachmentSystem.", 20, FontStyle.Normal, TextDim);
                return;
            }

            _titleText.text = _shownWeapon.weaponName.ToUpperInvariant();

            for (int s = 0; s < _shownWeapon.slots.Count; s++)
            {
                AttachmentSlot slot = _shownWeapon.slots[s];
                AddSectionHeader(slot.slotName);

                AddOptionCard("БЕЗ ОБВЕСА", s, -1);
                for (int o = 0; o < slot.options.Count; o++)
                {
                    AddOptionCard(slot.options[o].displayName.ToUpperInvariant(), s, o);
                }

                AddSpacer(14);
                AddSectionHeader("Положение");
                int slotIndex = s;
                AddNudgeRow(("ВЫШЕ", () => Nudge(slotIndex, Vector3.up)),
                            ("НИЖЕ", () => Nudge(slotIndex, Vector3.down)),
                            ("ВПЕРЁД", () => Nudge(slotIndex, Vector3.forward)),
                            ("НАЗАД", () => Nudge(slotIndex, Vector3.back)));
                AddNudgeRow(("ВЛЕВО", () => Nudge(slotIndex, Vector3.left)),
                            ("ВПРАВО", () => Nudge(slotIndex, Vector3.right)),
                            ("СБРОС", () => _shownWeapon.Nudge(slotIndex, null)));
            }

            RefreshSelection();
        }

        private void AddSectionHeader(string text)
        {
            RectTransform row = CreateRect("Header", _optionsRoot);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
            Text t = CreateText("Text", row, text.ToUpperInvariant(), 22, TextAnchor.MiddleLeft, FontStyle.Bold);
            t.color = Accent;
            SetRect(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(0, 4), Vector2.zero);
            Line(row, new Vector2(0, 0), new Vector2(0, 2), new Vector2(0, 1), AccentA(0.35f), new Vector2(0, 0), stretchX: true);
            Line(row, new Vector2(0, 0), new Vector2(0, 1), new Vector2(48, 3), Accent, new Vector2(0, 0));
        }

        private void AddSpacer(float h)
        {
            RectTransform r = CreateRect("Spacer", _optionsRoot);
            r.gameObject.AddComponent<LayoutElement>().preferredHeight = h;
        }

        private void AddLabel(string text, int size, FontStyle style, Color color)
        {
            Text label = CreateText("Label", _optionsRoot, text, size, TextAnchor.MiddleLeft, style);
            label.color = color;
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = size + 18;
        }

        private void AddOptionCard(string label, int slot, int option)
        {
            RectTransform rect = CreateRect("Option_" + label, _optionsRoot);
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight = 62;

            var card = new OptionCard { slot = slot, option = option };
            card.fill = rect.gameObject.AddComponent<Image>();
            card.fill.color = Color.white;
            card.button = rect.gameObject.AddComponent<Button>();
            card.button.targetGraphic = card.fill;
            card.border = Frame(rect, Accent, 1f);

            RectTransform bar = CreateRect("Bar", rect);
            SetRect(bar, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0), new Vector2(5, 0));
            card.bar = bar.gameObject.AddComponent<Image>();
            card.bar.raycastTarget = false;

            card.label = CreateText("Text", rect, label, 24, TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(card.label.rectTransform, Vector2.zero, Vector2.one, new Vector2(26, 0), new Vector2(-150, 0));

            card.status = CreateText("Status", rect, "", 15, TextAnchor.MiddleRight, FontStyle.Bold);
            SetRect(card.status.rectTransform, Vector2.zero, Vector2.one, new Vector2(0, 0), new Vector2(-20, 0));

            card.button.onClick.AddListener(() =>
            {
                if (_shownWeapon != null) _shownWeapon.Equip(slot, option);
            });
            _cards.Add(card);
        }

        private void RefreshSelection()
        {
            if (_shownWeapon == null) return;
            foreach (OptionCard card in _cards)
            {
                bool selected = _shownWeapon.GetCurrentOption(card.slot) == card.option;

                ColorBlock colors = card.button.colors;
                colors.colorMultiplier = 1f;
                colors.fadeDuration = 0.08f;
                Color baseFill = selected ? AccentA(0.2f) : new Color(1f, 1f, 1f, 0.03f);
                colors.normalColor = baseFill;
                colors.selectedColor = baseFill;
                colors.highlightedColor = selected ? AccentA(0.28f) : AccentA(0.1f);
                colors.pressedColor = AccentA(0.35f);
                card.button.colors = colors;
                card.fill.color = Color.white;

                foreach (Image line in card.border) line.color = selected ? Accent : AccentA(0.22f);
                card.bar.color = selected ? Accent : new Color(0, 0, 0, 0);
                card.label.color = selected ? Color.white : new Color(1f, 1f, 1f, 0.75f);
                card.status.text = selected ? "● УСТАНОВЛЕНО" : "";
                card.status.color = Accent;
            }
        }

        private float NudgeStep()
        {
            if (ShiftHeld()) return 0.001f; // 1 мм
            // Шаг = воксель прицела, чтобы прицел оставался на сетке оружия.
            float voxel = _shownWeapon != null ? _shownWeapon.SightVoxelSize : 0f;
            return voxel > 0f ? voxel : 0.005f;
        }

        private void Nudge(int slot, Vector3 dir)
        {
            if (_shownWeapon == null) return;
            _shownWeapon.Nudge(slot, dir * NudgeStep());
        }

        private void AddNudgeRow(params (string label, UnityEngine.Events.UnityAction action)[] buttons)
        {
            RectTransform row = CreateRect("NudgeRow", _optionsRoot);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 46;
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
                image.color = Color.white;
                var button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                ColorBlock colors = button.colors;
                colors.normalColor = new Color(1f, 1f, 1f, 0.03f);
                colors.highlightedColor = AccentA(0.14f);
                colors.selectedColor = new Color(1f, 1f, 1f, 0.03f);
                colors.pressedColor = AccentA(0.32f);
                colors.fadeDuration = 0.06f;
                button.colors = colors;
                button.onClick.AddListener(action);
                Frame(rect, AccentA(0.3f), 1f);

                Text text = CreateText("Text", rect, label, 17, TextAnchor.MiddleCenter, FontStyle.Bold);
                text.color = new Color(1f, 1f, 1f, 0.85f);
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

        // ------------------------------------------------------- HUD-графика

        /// <summary>Тонкая рамка из 4 линий.</summary>
        private Image[] Frame(RectTransform r, Color c, float t)
        {
            return new[]
            {
                EdgeLine(r, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -t), Vector2.zero, c),
                EdgeLine(r, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, t), c),
                EdgeLine(r, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(t, 0), c),
                EdgeLine(r, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-t, 0), Vector2.zero, c),
            };
        }

        /// <summary>Уголки-скобки как у прицельной сетки HUD.</summary>
        private void Brackets(RectTransform r, Color c, float len, float t)
        {
            foreach (Vector2 corner in new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1) })
            {
                float sx = corner.x == 0 ? 1 : -1;
                float sy = corner.y == 0 ? 1 : -1;
                Image h = EdgeLine(r, corner, corner, Vector2.zero, Vector2.zero, c);
                h.rectTransform.pivot = corner;
                h.rectTransform.sizeDelta = new Vector2(len, t);
                h.rectTransform.anchoredPosition = new Vector2(-sx * 6, -sy * 6);
                Image v = EdgeLine(r, corner, corner, Vector2.zero, Vector2.zero, c);
                v.rectTransform.pivot = corner;
                v.rectTransform.sizeDelta = new Vector2(t, len);
                v.rectTransform.anchoredPosition = new Vector2(-sx * 6, -sy * 6);
            }
        }

        private Image EdgeLine(RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, Color c)
        {
            RectTransform r = CreateRect("Line", parent);
            SetRect(r, aMin, aMax, oMin, oMax);
            var img = r.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        private void Line(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color c,
                          Vector2? pivot = null, bool stretchX = false)
        {
            RectTransform r = CreateRect("Line", parent);
            if (stretchX)
            {
                r.anchorMin = new Vector2(0, anchor.y);
                r.anchorMax = new Vector2(1, anchor.y);
            }
            else
            {
                r.anchorMin = r.anchorMax = anchor;
            }
            r.pivot = pivot ?? new Vector2(0, 1);
            r.sizeDelta = size;
            r.anchoredPosition = pos;
            var img = r.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
        }

        private static Texture2D BuildVignette()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Sqrt(dx * dx + dy * dy) - 0.45f) / 0.9f));
                tex.SetPixel(x, y, new Color(0, 0, 0, a));
            }
            tex.Apply();
            return tex;
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
