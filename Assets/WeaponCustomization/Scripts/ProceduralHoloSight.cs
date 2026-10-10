using UnityEngine;
using UnityEngine.Rendering;

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

        public static GameObject Build(Material template = null)
        {
            EnsureMaterials(template);

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
            if (_glassMat != null)
            {
                Quad(root.transform, "Glass", new Vector3(0f, WindowHeight, hoodZ + hoodLen * 0.5f - 0.002f),
                    new Vector3(0.039f, 0.038f, 1f), _glassMat);
            }

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

        /// <param name="template">Материал самого оружия: если шейдер под текущий рендер-пайплайн
        /// не найден, берётся его копия — он гарантированно рисуется (а не розовым).</param>
        private static void EnsureMaterials(Material template)
        {
            string rp = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.GetType().Name
                : "";
            bool urp = rp.Contains("Universal");
            bool hdrp = rp.Contains("HD");

            if (_bodyMat == null)
            {
                Shader lit = urp ? Shader.Find("Universal Render Pipeline/Lit")
                    : hdrp ? Shader.Find("HDRP/Lit")
                    : Shader.Find("Standard");

                if (lit != null) _bodyMat = new Material(lit);
                else if (template != null) _bodyMat = new Material(template);
                else _bodyMat = new Material(Shader.Find("Hidden/InternalErrorShader"));

                _bodyMat.name = "HoloSight_Body";
                Texture2D bodyTex = BuildBodyTexture(128);
                Color tint = Color.white;
                SetColor(_bodyMat, tint);
                SetTexture(_bodyMat, bodyTex);
                foreach (string prop in new[] { "_Smoothness", "_Glossiness" })
                    if (_bodyMat.HasProperty(prop)) _bodyMat.SetFloat(prop, 0.3f);
                foreach (string prop in new[] { "_Metallic" })
                    if (_bodyMat.HasProperty(prop)) _bodyMat.SetFloat(prop, 0.2f);
                // Убираем чужие карты нормалей/металличности, если скопировали материал оружия.
                foreach (string prop in new[] { "_BumpMap", "_NormalMap", "_MetallicGlossMap", "_MaskMap", "_OcclusionMap", "_EmissionMap" })
                    if (_bodyMat.HasProperty(prop)) _bodyMat.SetTexture(prop, null);
            }

            // Sprites/Default — прозрачный unlit, рисуется и в Built-in, и в URP.
            // В HDRP его нет — там стекло пропускаем, а сетку рисуем непрозрачным HDRP/Unlit.
            Shader sprite = hdrp ? null : Shader.Find("Sprites/Default");

            if (_glassMat == null && sprite != null)
            {
                _glassMat = new Material(sprite) { name = "HoloSight_Glass", color = new Color(0.55f, 0.75f, 0.85f, 0.12f) };
                _glassMat.mainTexture = Texture2D.whiteTexture;
            }

            if (_reticleMat == null)
            {
                Texture2D reticle = BuildReticleTexture(256);
                if (sprite != null)
                {
                    _reticleMat = new Material(sprite) { color = new Color(1f, 0.15f, 0.1f, 1f) };
                    _reticleMat.mainTexture = reticle;
                    _reticleMat.renderQueue = 3100; // поверх стекла
                }
                else
                {
                    Shader unlit = Shader.Find("HDRP/Unlit");
                    _reticleMat = unlit != null ? new Material(unlit) : new Material(_bodyMat);
                    SetColor(_reticleMat, new Color(1f, 0.15f, 0.1f, 1f));
                    SetTexture(_reticleMat, reticle);
                    if (_reticleMat.HasProperty("_AlphaCutoffEnable")) _reticleMat.SetFloat("_AlphaCutoffEnable", 1f);
                    if (_reticleMat.HasProperty("_AlphaCutoff")) _reticleMat.SetFloat("_AlphaCutoff", 0.5f);
                    _reticleMat.EnableKeyword("_ALPHATEST_ON");
                }
                _reticleMat.name = "HoloSight_Reticle";
            }
        }

        private static void SetColor(Material m, Color c)
        {
            foreach (string prop in new[] { "_BaseColor", "_Color", "_UnlitColor" })
                if (m.HasProperty(prop)) m.SetColor(prop, c);
        }

        private static void SetTexture(Material m, Texture t)
        {
            foreach (string prop in new[] { "_BaseMap", "_MainTex", "_BaseColorMap", "_UnlitColorMap" })
                if (m.HasProperty(prop)) m.SetTexture(prop, t);
        }

        /// <summary>Тёмно-серый матовый металл с лёгким шумом и фаской по краям граней.</summary>
        private static Texture2D BuildBodyTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "HoloSightBody",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            var rnd = new System.Random(16);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float edge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y)) / (size * 0.06f);
                    float bevel = Mathf.Lerp(0.07f, 0f, Mathf.Clamp01(edge)); // светлая фаска
                    float noise = (float)rnd.NextDouble() * 0.025f;
                    float v = 0.11f + noise + bevel;
                    byte b = (byte)(Mathf.Clamp01(v) * 255);
                    pixels[y * size + x] = new Color32(b, b, (byte)(b + 2), 255);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
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
