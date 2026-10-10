using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace WeaponCustomization
{
    /// <summary>
    /// Один вариант обвеса для слота (например, "Голографический прицел").
    /// </summary>
    [Serializable]
    public class AttachmentOption
    {
        public string displayName = "Голографический прицел";

        [Tooltip("Префаб обвеса. Если пусто и включён Use Procedural Holo Sight — " +
                 "прицел будет сгенерирован кодом.")]
        public GameObject prefab;

        [Tooltip("Сгенерировать простой голографический прицел, если префаб не назначен.")]
        public bool useProceduralHoloSight = true;

        [Header("Смещение относительно точки крепления")]
        public Vector3 localPosition = Vector3.zero;
        public Vector3 localEulerAngles = Vector3.zero;
        public Vector3 localScale = Vector3.one;
    }

    /// <summary>
    /// Слот обвеса (прицел, глушитель, рукоять...). Сейчас используется один — "Прицел".
    /// </summary>
    [Serializable]
    public class AttachmentSlot
    {
        public string slotName = "Прицел";

        [Tooltip("Точка на планке Picatinny, куда ставится обвес (пустой объект-ребёнок оружия).")]
        public Transform mountPoint;

        [Tooltip("Объекты, которые нужно скрыть, когда обвес установлен (например, механический целик/мушка).")]
        public GameObject[] hideWhenAttached;

        [Tooltip("Переносить ли Aim Point оружия на точку прицеливания обвеса.")]
        public bool movesAimPoint = true;

        public List<AttachmentOption> options = new List<AttachmentOption> { new AttachmentOption() };

        [Tooltip("Какой вариант установлен при старте. -1 = ничего.")]
        public int defaultOption = -1;

        [NonSerialized] public int currentOption = -1;
        [NonSerialized] public GameObject currentInstance;
    }

    [Serializable] public class AttachmentChangedEvent : UnityEvent<int, int> { }

    /// <summary>
    /// Вешается на корень оружия (MX16A4). Устанавливает/снимает обвес и
    /// переносит точку прицеливания KINEMATION на прицел.
    /// </summary>
    public class WeaponAttachmentSystem : MonoBehaviour
    {
        public string weaponName = "MX16A4";

        [Tooltip("Aim Point оружия, который использует KINEMATION для выравнивания при прицеливании " +
                 "(обычно дочерний объект оружия \"AimPoint\").")]
        public Transform aimPoint;

        public List<AttachmentSlot> slots = new List<AttachmentSlot> { new AttachmentSlot() };

        [Tooltip("Вызывается после смены обвеса: (индекс слота, индекс варианта, -1 = снят). " +
                 "Сюда можно повесить обновление прицеливания в вашем контроллере KINEMATION.")]
        public AttachmentChangedEvent onAttachmentChanged = new AttachmentChangedEvent();

        /// <summary>Имя дочернего объекта в префабе обвеса, задающего точку прицеливания.</summary>
        public const string SightAimPointName = "AimPoint";

        private Vector3 _defaultAimLocalPos;
        private Quaternion _defaultAimLocalRot;
        private bool _initialized;

        public event Action Changed;

        private void Awake()
        {
            Init();
        }

        private void Start()
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].defaultOption >= 0) Equip(i, slots[i].defaultOption);
            }
        }

        private void Init()
        {
            // aimPoint может быть назначен позже (автонастройка) — запоминаем исходное положение при первой возможности.
            if (_initialized || aimPoint == null) return;
            _initialized = true;
            _defaultAimLocalPos = aimPoint.localPosition;
            _defaultAimLocalRot = aimPoint.localRotation;
        }

        public int GetCurrentOption(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < slots.Count ? slots[slotIndex].currentOption : -1;
        }

        /// <summary>Установить вариант optionIndex в слот slotIndex. optionIndex = -1 снимает обвес.</summary>
        public void Equip(int slotIndex, int optionIndex)
        {
            Init();
            if (slotIndex < 0 || slotIndex >= slots.Count) return;

            AttachmentSlot slot = slots[slotIndex];
            if (optionIndex >= slot.options.Count) optionIndex = -1;
            if (slot.currentOption == optionIndex && (optionIndex < 0 || slot.currentInstance != null)) return;

            if (slot.currentInstance != null)
            {
                // Destroy отложен до конца кадра — сразу прячем, чтобы старый обвес не попал в превью.
                slot.currentInstance.SetActive(false);
                Destroy(slot.currentInstance);
            }
            slot.currentInstance = null;
            slot.currentOption = optionIndex;

            if (optionIndex >= 0)
            {
                slot.currentInstance = Spawn(slot, slot.options[optionIndex]);
            }

            bool attached = slot.currentInstance != null;
            if (slot.hideWhenAttached != null)
            {
                foreach (GameObject go in slot.hideWhenAttached)
                {
                    if (go != null) go.SetActive(!attached);
                }
            }

            if (slot.movesAimPoint) UpdateAimPoint(slot);

            onAttachmentChanged.Invoke(slotIndex, optionIndex);
            Changed?.Invoke();
        }

        /// <summary>
        /// Автонастройка оружия без ручной работы: точка крепления ставится на верх модели
        /// вдоль самой длинной оси (ствол), Aim Point ищется среди детей по имени "aim".
        /// </summary>
        public static WeaponAttachmentSystem AutoSetup(GameObject weaponRoot)
        {
            var existing = weaponRoot.GetComponent<WeaponAttachmentSystem>();
            if (existing != null) return existing;

            Transform root = weaponRoot.transform;

            // Габариты модели в локальных координатах оружия.
            bool any = false;
            Bounds local = new Bounds();
            foreach (Renderer r in weaponRoot.GetComponentsInChildren<Renderer>(false))
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                Bounds wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = wb.center + Vector3.Scale(wb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = root.InverseTransformPoint(corner);
                    if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                    else local.Encapsulate(p);
                }
            }

            var mount = new GameObject("SightMount (auto)").transform;
            mount.SetParent(root, false);
            if (any)
            {
                // Оси модели могут быть любыми (FBX часто повёрнут), поэтому определяем их по миру:
                // "вверх" — локальная ось, ближайшая к мировому верху; "ствол" — ось, ближайшая к взгляду
                // камеры (оружие в руках смотрит туда же, куда игрок).
                Vector3 upLocal = SnapToAxis(root.InverseTransformDirection(Vector3.up));
                Camera cam = Camera.main;
                Vector3 look = cam != null ? cam.transform.forward : root.forward;
                look -= Vector3.Project(look, root.TransformDirection(upLocal));
                Vector3 barrelLocal = SnapToAxis(root.InverseTransformDirection(look), upLocal);

                // Верх модели над центром, чуть ближе к прикладу — над ствольной коробкой.
                Vector3 pos = local.center;
                pos += upLocal * Vector3.Scale(local.extents, Abs(upLocal)).magnitude;
                pos -= barrelLocal * Vector3.Scale(local.extents, Abs(barrelLocal)).magnitude * 0.1f;
                mount.localPosition = pos;
                mount.localRotation = Quaternion.LookRotation(barrelLocal, upLocal);
                // Сам прицел в метрах, а модель могла быть отмасштабирована.
                Vector3 ls = root.lossyScale;
                mount.localScale = new Vector3(1f / Mathf.Abs(ls.x), 1f / Mathf.Abs(ls.y), 1f / Mathf.Abs(ls.z));
            }

            Transform aim = null;
            foreach (Transform t in weaponRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t != root && t.name.ToLowerInvariant().Contains("aim")) { aim = t; break; }
            }

            var sys = weaponRoot.AddComponent<WeaponAttachmentSystem>();
            sys.weaponName = weaponRoot.name.Replace("(Clone)", "").Trim();
            sys.aimPoint = aim;
            sys.slots[0].mountPoint = mount;
            Debug.Log("[WeaponCustomization] Оружие '" + sys.weaponName + "' настроено автоматически. Aim Point: " +
                      (aim != null ? aim.name : "не найден"));
            return sys;
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>Округлить направление до ближайшей локальной оси (±X/±Y/±Z), исключая exclude.</summary>
        private static Vector3 SnapToAxis(Vector3 dir, Vector3 exclude = default)
        {
            Vector3 best = Vector3.forward;
            float bestDot = float.NegativeInfinity;
            foreach (Vector3 axis in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
            {
                if (exclude != Vector3.zero && Mathf.Abs(Vector3.Dot(axis, exclude)) > 0.5f) continue;
                float d = Vector3.Dot(axis, dir);
                if (d > bestDot) { bestDot = d; best = axis; }
            }
            return best;
        }

        // ------------------------------------------------------------ ручная подгонка

        private string OffsetKey(int slotIndex) => "WeaponCustomization_" + weaponName + "_" + slotIndex;

        public Vector3 GetUserOffset(int slotIndex)
        {
            string v = PlayerPrefs.GetString(OffsetKey(slotIndex), "");
            string[] p = v.Split(';');
            if (p.Length == 3 &&
                float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                return new Vector3(x, y, z);
            return Vector3.zero;
        }

        /// <summary>
        /// Сдвинуть обвес в слоте (в метрах, оси точки крепления: X — вбок, Y — вверх, Z — вперёд).
        /// Сдвиг сохраняется между запусками. delta = null сбрасывает его.
        /// </summary>
        public void Nudge(int slotIndex, Vector3? delta)
        {
            if (slotIndex < 0 || slotIndex >= slots.Count) return;
            Vector3 offset = delta.HasValue ? GetUserOffset(slotIndex) + delta.Value : Vector3.zero;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            PlayerPrefs.SetString(OffsetKey(slotIndex),
                offset.x.ToString("F4", ci) + ";" + offset.y.ToString("F4", ci) + ";" + offset.z.ToString("F4", ci));
            PlayerPrefs.Save();

            AttachmentSlot slot = slots[slotIndex];
            if (slot.currentInstance != null && slot.currentOption >= 0)
            {
                slot.currentInstance.transform.localPosition = slot.options[slot.currentOption].localPosition + offset;
                if (slot.movesAimPoint) UpdateAimPoint(slot);
            }
            Debug.Log("[WeaponCustomization] Сдвиг '" + slot.slotName + "': " + offset.ToString("F4") +
                      " (можно вписать в Local Position варианта обвеса)");
            Changed?.Invoke();
        }

        private GameObject Spawn(AttachmentSlot slot, AttachmentOption option)
        {
            Transform parent = slot.mountPoint != null ? slot.mountPoint : transform;

            GameObject instance;
            if (option.prefab != null)
            {
                instance = Instantiate(option.prefab, parent, false);
            }
            else if (option.useProceduralHoloSight)
            {
                Renderer weaponRenderer = GetComponentInChildren<Renderer>();
                instance = ProceduralHoloSight.Build(weaponRenderer != null ? weaponRenderer.sharedMaterial : null);
                instance.transform.SetParent(parent, false);
            }
            else
            {
                return null;
            }

            instance.name = option.displayName;
            instance.transform.localPosition = option.localPosition + GetUserOffset(slots.IndexOf(slot));
            instance.transform.localRotation = Quaternion.Euler(option.localEulerAngles);
            instance.transform.localScale = option.localScale;
            SetLayerRecursively(instance, parent.gameObject.layer);
            return instance;
        }

        private void UpdateAimPoint(AttachmentSlot slot)
        {
            if (aimPoint == null) return;

            Transform sightAim = slot.currentInstance != null
                ? FindDeep(slot.currentInstance.transform, SightAimPointName)
                : null;

            if (sightAim != null)
            {
                aimPoint.SetPositionAndRotation(sightAim.position, sightAim.rotation);
            }
            else
            {
                aimPoint.localPosition = _defaultAimLocalPos;
                aimPoint.localRotation = _defaultAimLocalRot;
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }
    }
}
