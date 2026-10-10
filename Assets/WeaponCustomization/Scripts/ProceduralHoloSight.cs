using UnityEngine;

namespace WeaponCustomization
{
    /// <summary>
    /// Генерирует простой голографический прицел (в стиле EOTech) из примитивов,
    /// чтобы система работала без отдельной 3D-модели. Ось +Z — направление ствола,
    /// +Y — вверх, пивот — низ крепления на планке.
    /// Если у вас есть своя модель — назначьте её в AttachmentOption.prefab
    /// и добавьте в неё пустой дочерний объект "AimPoint" в центре сетки.
    /// </summary>
    public static class ProceduralHoloSight
    {
        private static Material _bodyMat;
        private static Material _glassMat;
        private static Material _reticleMat;

        // Высота центра окна над планкой (м).
        public const float WindowHeight = 0.038f;

        public static GameObject Build()
        {
            EnsureMaterials();

            var root = new GameObject("HoloSight");

            // Крепление на планку
            Box(root.transform, "Mount", new Vector3(0f, 0.006f, 0f), new Vector3(0.032f, 0.012f, 0.085f), _bodyMat);
            // Корпус с батарейным отсеком сзади
            Box(root.transform, "Base", new Vector3(0f, 0.015f, -0.005f), new Vector3(0.042f, 0.008f, 0.095f), _bodyMat);
            Box(root.transform, "Battery", new Vector3(0f, 0.026f, 0.03f), new Vector3(0.036f, 0.016f, 0.03f), _bodyMat);

            // "Капюшон" окна
            const float hoodLen = 0.05f;
            const float hoodZ = -0.022f;
            Box(root.transform, "HoodLeft", new Vector3(-0.0215f, WindowHeight, hoodZ), new Vector3(0.004f, 0.038f, hoodLen), _bodyMat);
            Box(root.transform, "HoodRight", new Vector3(0.0215f, WindowHeight, hoodZ), new Vector3(0.004f, 0.038f, hoodLen), _bodyMat);
            Box(root.transform, "HoodTop", new Vector3(0f, WindowHeight + 0.021f, hoodZ), new Vector3(0.047f, 0.004f, hoodLen), _bodyMat);

            // Стекло (лёгкий голубоватый тон)
            GameObject glass = Quad(root.transform, "Glass", new Vector3(0f, WindowHeight, hoodZ + hoodLen * 0.5f - 0.002f),
                new Vector3(0.039f, 0.038f, 1f), _glassMat);
            glass.transform.localRotation = Quaternion.identity;

            // Сетка: кольцо + точка. Квад смотрит назад (-Z), т.е. на стрелка.
            GameObject reticle = Quad(root.transform, "Reticle", new Vector3(0f, WindowHeight, hoodZ),
                new Vector3(0.022f, 0.022f, 1f), _reticleMat);
            reticle.transform.localRotation = Quaternion.identity;

            // Точка прицеливания — её использует WeaponAttachmentSystem для переноса Aim Point.
            var aim = new GameObject(WeaponAttachmentSystem.SightAimPointName);
            aim.transform.SetParent(root.transform, false);
            aim.transform.localPosition = new Vector3(0f, WindowHeight, hoodZ);

            return root;
        }

        private static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private static GameObject Quad(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        private static void EnsureMaterials()
        {
            if (_bodyMat == null)
            {
                Shader lit = FindShader("Universal Render Pipeline/Lit", "HDRP/Lit", "Standard");
                _bodyMat = new Material(lit) { name = "HoloSight_Body" };
                Color body = new Color(0.08f, 0.08f, 0.085f);
                _bodyMat.color = body;
                if (_bodyMat.HasProperty("_BaseColor")) _bodyMat.SetColor("_BaseColor", body);
                if (_bodyMat.HasProperty("_Smoothness")) _bodyMat.SetFloat("_Smoothness", 0.35f);
                if (_bodyMat.HasProperty("_Glossiness")) _bodyMat.SetFloat("_Glossiness", 0.35f);
            }

            // Sprites/Default — прозрачный unlit, рисуется и в Built-in, и в URP.
            Shader sprite = FindShader("Sprites/Default", "Unlit/Transparent");

            if (_glassMat == null)
            {
                _glassMat = new Material(sprite) { name = "HoloSight_Glass", color = new Color(0.55f, 0.75f, 0.85f, 0.12f) };
                _glassMat.mainTexture = Texture2D.whiteTexture;
            }

            if (_reticleMat == null)
            {
                _reticleMat = new Material(sprite) { name = "HoloSight_Reticle", color = new Color(1f, 0.15f, 0.1f, 1f) };
                _reticleMat.mainTexture = BuildReticleTexture(256);
                _reticleMat.renderQueue = 3100; // поверх стекла
            }
        }

        private static Shader FindShader(params string[] names)
        {
            foreach (string n in names)
            {
                Shader s = Shader.Find(n);
                if (s != null) return s;
            }
            return Shader.Find("Hidden/InternalErrorShader");
        }

        /// <summary>Классическая сетка EOTech: кольцо 65 MOA + точка 1 MOA.</summary>
        private static Texture2D BuildReticleTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "HoloReticle",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear
            };

            float c = (size - 1) * 0.5f;
            float ringR = size * 0.42f;
            float ringW = size * 0.025f;
            float dotR = size * 0.03f;
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float ring = 1f - Mathf.Clamp01((Mathf.Abs(d - ringR) - ringW) / 1.5f);
                    float dot = 1f - Mathf.Clamp01((d - dotR) / 1.5f);
                    float a = Mathf.Max(ring, dot);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }
    }
}
