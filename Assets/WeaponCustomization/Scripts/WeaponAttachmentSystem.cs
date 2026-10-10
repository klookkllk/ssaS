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
            if (_initialized) return;
            _initialized = true;
            if (aimPoint != null)
            {
                _defaultAimLocalPos = aimPoint.localPosition;
                _defaultAimLocalRot = aimPoint.localRotation;
            }
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
                instance = ProceduralHoloSight.Build();
                instance.transform.SetParent(parent, false);
            }
            else
            {
                return null;
            }

            instance.name = option.displayName;
            instance.transform.localPosition = option.localPosition;
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
