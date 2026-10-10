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

        private void Start()
        {
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
                if (disableWhileOpen != null)
                {
                    foreach (Behaviour b in disableWhileOpen)
                    {
                        if (b != null && b.enabled) { b.enabled = false; _disabledByMenu.Add(b); }
                    }
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
            foreach (WeaponAttachmentSystem w in FindObjectsOfType<WeaponAttachmentSystem>())
            {
                if (w.isActiveAndEnabled) return w;
            }
            return weapon;
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
            }

            RefreshSelection();
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
            if (EventSystem.current != null || FindObjectOfType<EventSystem>() != null) return;
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
