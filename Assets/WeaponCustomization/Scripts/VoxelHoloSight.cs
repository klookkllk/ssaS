using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeaponCustomization
{
    /// <summary>
    /// Воксельный голографический прицел в стиле EOTech EXPS3, собранный из воксельной сетки
    /// в один меш с палитровой текстурой — так же, как устроены воксельные модели (MagicaVoxel).
    ///
    /// Чтобы прицел выглядел "родным" для оружия:
    ///  • размер вокселя берётся из меша оружия (или его делитель — для детализации);
    ///  • цвета корпуса снимаются с текстуры оружия;
    ///  • используется копия материала оружия (тот же шейдер и освещение), меняется только текстура.
    ///
    /// Оси: +Z — к дулу, +Y — вверх, пивот — низ крепления, по центру.
    /// </summary>
    public static class VoxelHoloSight
    {
        // Размер сетки в вокселях: ширина (X), высота (Y), длина (Z).
        private const int W = 11, H = 13, L = 15;

        // Индексы цветов палитры.
        private const byte Empty = 0;
        private const byte Body = 1;      // основной корпус
        private const byte BodyDark = 2;  // крепление, нижние части
        private const byte Steel = 3;     // винты, кромки окна
        private const byte Rubber = 4;    // кнопки, накладки
        private const byte Accent = 5;    // шильдик/маркировка
        private const byte Lens = 6;      // тёмное стекло излучателя
        private const byte Emitter = 7;   // красный светодиод излучателя
        private const int ColorCount = 8;
        private const int Shades = 4;     // тени/вариации на каждый цвет

        public const string AimPointName = WeaponAttachmentSystem.SightAimPointName;

        /// <summary>Высота центра окна над планкой — в вокселях.</summary>
        private const float WindowCenterY = 8.5f;
        private const float ReticleZ = 8f;

        public static GameObject Build(Material weaponMaterial, float voxelSize)
        {
            byte[,,] grid = BuildGrid();
            Color[] baseColors = PickColors(weaponMaterial);
            Texture2D palette = BuildPalette(baseColors);

            var root = new GameObject("VoxelHoloSight");

            // Корпус
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = BuildMesh(grid, voxelSize);
            var mr = body.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MakeBodyMaterial(weaponMaterial, palette);

            // Окно: стекло + голографическая сетка
            float vs = voxelSize;
            Vector3 windowCenter = new Vector3(0f, WindowCenterY * vs, 0f);
            Material glass = MakeTransparent(new Color(0.45f, 0.75f, 0.8f, 0.14f), Texture2D.whiteTexture, 3000);
            Material reticle = MakeTransparent(new Color(1f, 0.12f, 0.08f, 1f), BuildReticleTexture(), 3100);

            if (glass != null)
            {
                // Переднее стекло в передней рамке и заднее (у стрелка).
                Quad(root.transform, "GlassFront", windowCenter + Vector3.forward * (12.6f - 7.5f) * vs, new Vector2(7f, 6f) * vs, glass);
                Quad(root.transform, "GlassRear", windowCenter + Vector3.forward * (5.4f - 7.5f) * vs, new Vector2(7f, 6f) * vs, glass);
            }
            if (reticle != null)
            {
                Quad(root.transform, "Reticle", windowCenter + Vector3.forward * (ReticleZ - 7.5f) * vs, new Vector2(4.2f, 4.2f) * vs, reticle);
            }

            var aim = new GameObject(AimPointName);
            aim.transform.SetParent(root.transform, false);
            aim.transform.localPosition = windowCenter + Vector3.forward * (ReticleZ - 7.5f) * vs;

            return root;
        }

        // ------------------------------------------------------------------ модель

        private static byte[,,] BuildGrid()
        {
            var g = new byte[W, H, L];

            // ── Крепление на Picatinny (ласточкин хвост + прижимные винты справа)
            Fill(g, 3, 7, 0, 0, 1, 13, BodyDark);         // зацеп снизу
            Fill(g, 2, 8, 1, 1, 1, 13, BodyDark);
            Fill(g, 2, 2, 0, 0, 1, 13, BodyDark);         // губки
            Fill(g, 8, 8, 0, 0, 1, 13, BodyDark);
            Fill(g, 9, 9, 0, 1, 3, 4, Steel);             // винт 1
            Fill(g, 9, 9, 0, 1, 10, 11, Steel);           // винт 2
            Fill(g, 10, 10, 1, 1, 3, 4, Steel);
            Fill(g, 10, 10, 1, 1, 10, 11, Steel);

            // ── Основание корпуса
            Fill(g, 1, 9, 2, 4, 0, 14, Body);
            Fill(g, 1, 9, 2, 2, 0, 0, BodyDark);          // скос сзади снизу
            Fill(g, 1, 9, 2, 2, 14, 14, BodyDark);        // скос спереди снизу

            // ── Батарейный отсек сзади (под окном) с крышкой
            Fill(g, 1, 9, 5, 6, 0, 4, Body);
            Fill(g, 2, 8, 5, 6, 0, 0, BodyDark);          // торец
            Fill(g, 4, 6, 7, 7, 1, 3, Body);              // горб крышки
            Fill(g, 5, 5, 7, 7, 0, 0, Steel);             // защёлка

            // ── Кнопки яркости на заднем торце
            Set(g, 3, 3, 0, Rubber); Set(g, 3, 4, 0, Rubber);
            Set(g, 7, 3, 0, Rubber); Set(g, 7, 4, 0, Rubber);
            Set(g, 5, 4, 0, Accent);                      // индикатор

            // ── Окно ("капюшон"): стенки, крыша, рамки спереди и сзади
            Fill(g, 1, 1, 5, 11, 5, 13, Body);            // левая стенка
            Fill(g, 9, 9, 5, 11, 5, 13, Body);            // правая стенка
            Fill(g, 1, 9, 12, 12, 5, 13, Body);           // крыша
            // Передняя рамка — толще, с выступающим козырьком
            Fill(g, 0, 1, 4, 12, 12, 14, Body);
            Fill(g, 9, 10, 4, 12, 12, 14, Body);
            Fill(g, 0, 10, 12, 12, 12, 14, Body);
            Fill(g, 2, 8, 4, 4, 12, 14, Body);
            // Кромки окна (металл)
            Fill(g, 2, 8, 11, 11, 13, 13, Steel);
            Fill(g, 2, 2, 5, 10, 13, 13, Steel);
            Fill(g, 8, 8, 5, 10, 13, 13, Steel);
            // Задняя рамка
            Fill(g, 2, 8, 11, 11, 5, 5, Body);
            Fill(g, 2, 2, 5, 10, 5, 5, Body);
            Fill(g, 8, 8, 5, 10, 5, 5, Body);
            // Вырез сверху козырька (как у EXPS)
            Fill(g, 4, 6, 12, 12, 7, 10, Empty);
            Fill(g, 3, 7, 12, 12, 8, 9, Empty);
            Fill(g, 4, 6, 11, 11, 7, 10, Body);           // дно выреза

            // ── Излучатель голограммы — у основания окна сзади
            Fill(g, 3, 7, 5, 5, 6, 7, BodyDark);
            Fill(g, 4, 6, 5, 5, 6, 6, Lens);
            Set(g, 5, 5, 7, Emitter);

            // ── Боковые накладки и маркировка
            Fill(g, 0, 0, 3, 4, 2, 11, Rubber);           // левая резиновая накладка
            Fill(g, 10, 10, 5, 7, 6, 9, Accent);          // кнопки сбоку (справа)
            Set(g, 10, 6, 7, Rubber); Set(g, 10, 6, 8, Rubber);
            Fill(g, 0, 0, 7, 8, 6, 10, Accent);           // шильдик слева
            Fill(g, 0, 0, 7, 7, 7, 9, Body);

            return g;
        }

        private static void Fill(byte[,,] g, int x0, int x1, int y0, int y1, int z0, int z1, byte c)
        {
            for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
                Set(g, x, y, z, c);
        }

        private static void Set(byte[,,] g, int x, int y, int z, byte c)
        {
            if (x >= 0 && x < W && y >= 0 && y < H && z >= 0 && z < L) g[x, y, z] = c;
        }

        private static bool Solid(byte[,,] g, int x, int y, int z)
        {
            return x >= 0 && x < W && y >= 0 && y < H && z >= 0 && z < L && g[x, y, z] != Empty;
        }

        // ------------------------------------------------------------------ меш

        private static readonly Vector3Int[] FaceDirs =
        {
            Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down,
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
        };

        // 4 угла каждой грани (единичный куб), по часовой стрелке при взгляде снаружи.
        private static readonly Vector3[][] FaceCorners =
        {
            new[] { new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1) },
            new[] { new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0) },
            new[] { new Vector3(0, 1, 0), new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0) },
            new[] { new Vector3(0, 0, 1), new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1) },
            new[] { new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1), new Vector3(0, 0, 1) },
            new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) },
        };

        private static Mesh BuildMesh(byte[,,] g, float vs)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            Vector3 origin = new Vector3(-W * 0.5f, 0f, -L * 0.5f);

            for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
            for (int z = 0; z < L; z++)
            {
                byte c = g[x, y, z];
                if (c == Empty) continue;

                for (int f = 0; f < 6; f++)
                {
                    Vector3Int d = FaceDirs[f];
                    if (Solid(g, x + d.x, y + d.y, z + d.z)) continue;

                    int shade = ShadeFor(g, x, y, z, f);
                    Vector2 uv = PaletteUV(c, shade);
                    int start = verts.Count;
                    foreach (Vector3 corner in FaceCorners[f])
                    {
                        verts.Add((origin + new Vector3(x, y, z) + corner) * vs);
                        normals.Add(d);
                        uvs.Add(uv);
                    }
                    tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
                    tris.Add(start); tris.Add(start + 2); tris.Add(start + 3);
                }
            }

            var mesh = new Mesh { name = "VoxelHoloSight" };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>
        /// Оттенок вокселя: 0 — тень, 1 — основной, 2 — светлый, 3 — блик на кромке.
        /// Кромки (воксель открыт сразу с двух сторон) подсвечиваются, низ и полости — темнее,
        /// плюс детерминированный "шум" — как ручная покраска в воксельных редакторах.
        /// </summary>
        private static int ShadeFor(byte[,,] g, int x, int y, int z, int face)
        {
            int open = 0;
            for (int f = 0; f < 6; f++)
            {
                Vector3Int d = FaceDirs[f];
                if (!Solid(g, x + d.x, y + d.y, z + d.z)) open++;
            }

            bool topEdge = !Solid(g, x, y + 1, z) && (open >= 3);
            if (topEdge && face != 3) return 3;
            if (face == 3) return 0;                          // низ — тень
            if (open >= 3) return 2;

            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791);
            h = (h ^ (h >> 13)) * 1274126177u;
            int r = (int)(h % 100);
            return r < 18 ? 0 : r < 82 ? 1 : 2;
        }

        // ------------------------------------------------------------------ палитра

        private static Vector2 PaletteUV(byte color, int shade)
        {
            // Текстура ColorCount x Shades, берём центр пикселя.
            return new Vector2((color + 0.5f) / ColorCount, (shade + 0.5f) / Shades);
        }

        private static Texture2D BuildPalette(Color[] baseColors)
        {
            var tex = new Texture2D(ColorCount, Shades, TextureFormat.RGBA32, false)
            {
                name = "VoxelHoloSightPalette",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            float[] shadeMul = { 0.72f, 1f, 1.18f, 1.45f };
            for (int c = 0; c < ColorCount; c++)
            {
                for (int s = 0; s < Shades; s++)
                {
                    Color col = baseColors[c];
                    // Красный светодиод и стекло не затемняем.
                    float m = c == Emitter || c == Lens ? 1f : shadeMul[s];
                    tex.SetPixel(c, s, new Color(Mathf.Clamp01(col.r * m + (s == 3 ? 0.02f : 0f)),
                                                 Mathf.Clamp01(col.g * m + (s == 3 ? 0.02f : 0f)),
                                                 Mathf.Clamp01(col.b * m + (s == 3 ? 0.02f : 0f)), 1f));
                }
            }
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>Цвета прицела, подобранные под текстуру оружия.</summary>
        private static Color[] PickColors(Material weaponMaterial)
        {
            Color body = new Color(0.16f, 0.16f, 0.17f);
            Color steel = new Color(0.42f, 0.42f, 0.44f);

            List<Color> pixels = ReadTexturePixels(GetMainTexture(weaponMaterial));
            if (pixels != null)
            {
                Color tint = GetTint(weaponMaterial);
                if (MostCommon(pixels, tint, 0.03f, 0.35f, 0.3f, out Color dark)) body = dark;
                if (MostCommon(pixels, tint, 0.3f, 0.75f, 0.2f, out Color mid)) steel = mid;
                if (Luminance(steel) < Luminance(body) * 1.4f) steel = body * 2.2f;
            }

            var colors = new Color[ColorCount];
            colors[Empty] = Color.black;
            colors[Body] = body;
            colors[BodyDark] = body * 0.7f;
            colors[Steel] = steel;
            colors[Rubber] = body * 0.5f;
            colors[Accent] = Color.Lerp(body, steel, 0.55f);
            colors[Lens] = new Color(0.05f, 0.09f, 0.12f);
            colors[Emitter] = new Color(1f, 0.15f, 0.08f);
            for (int i = 0; i < ColorCount; i++) colors[i].a = 1f;
            return colors;
        }

        private static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>Самый частый цвет в заданном диапазоне яркости и насыщенности (гистограмма 4 бита/канал).</summary>
        private static bool MostCommon(List<Color> pixels, Color tint, float minLum, float maxLum, float maxSat, out Color result)
        {
            var buckets = new Dictionary<int, (int count, Vector3 sum)>();
            foreach (Color p0 in pixels)
            {
                if (p0.a < 0.5f) continue;
                Color p = p0 * tint;
                float lum = Luminance(p);
                Color.RGBToHSV(p, out _, out float sat, out _);
                if (lum < minLum || lum > maxLum || sat > maxSat) continue;
                int key = ((int)(p.r * 15) << 8) | ((int)(p.g * 15) << 4) | (int)(p.b * 15);
                buckets.TryGetValue(key, out var b);
                buckets[key] = (b.count + 1, b.sum + new Vector3(p.r, p.g, p.b));
            }

            result = default;
            int best = 0;
            foreach (var kv in buckets)
            {
                if (kv.Value.count <= best) continue;
                best = kv.Value.count;
                Vector3 avg = kv.Value.sum / kv.Value.count;
                result = new Color(avg.x, avg.y, avg.z, 1f);
            }
            return best > 0;
        }

        private static readonly string[] TextureProps = { "_BaseMap", "_MainTex", "_BaseColorMap", "_UnlitColorMap", "_Albedo" };
        private static readonly string[] ColorProps = { "_BaseColor", "_Color", "_UnlitColor" };

        private static Texture GetMainTexture(Material m)
        {
            if (m == null) return null;
            foreach (string p in TextureProps)
                if (m.HasProperty(p) && m.GetTexture(p) != null) return m.GetTexture(p);
            return null;
        }

        private static Color GetTint(Material m)
        {
            foreach (string p in ColorProps)
                if (m.HasProperty(p)) return m.GetColor(p);
            return Color.white;
        }

        /// <summary>Читает пиксели любой текстуры, даже без Read/Write — через RenderTexture.</summary>
        private static List<Color> ReadTexturePixels(Texture src)
        {
            if (src == null) return null;
            int w = Mathf.Clamp(src.width, 1, 256);
            int h = Mathf.Clamp(src.height, 1, 256);
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tmp.Apply();
                var list = new List<Color>(tmp.GetPixels());
                Object.Destroy(tmp);
                return list;
            }
            catch
            {
                return null;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // ------------------------------------------------------------------ материалы

        private static Material MakeBodyMaterial(Material weaponMaterial, Texture2D palette)
        {
            Material m;
            if (weaponMaterial != null)
            {
                // Тот же шейдер, что у оружия — одинаковое освещение и "ощущение" материала.
                m = new Material(weaponMaterial);
                foreach (string p in new[] { "_BumpMap", "_NormalMap", "_MetallicGlossMap", "_SpecGlossMap",
                                             "_MaskMap", "_OcclusionMap", "_EmissionMap", "_DetailAlbedoMap" })
                    if (m.HasProperty(p)) m.SetTexture(p, null);
                foreach (string p in ColorProps)
                    if (m.HasProperty(p)) m.SetColor(p, Color.white);
            }
            else
            {
                string rp = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.GetType().Name : "";
                Shader s = rp.Contains("Universal") ? Shader.Find("Universal Render Pipeline/Lit")
                    : rp.Contains("HD") ? Shader.Find("HDRP/Lit")
                    : Shader.Find("Standard");
                m = new Material(s);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.25f);
            }

            foreach (string p in TextureProps)
            {
                if (!m.HasProperty(p)) continue;
                m.SetTexture(p, palette);
                m.SetTextureScale(p, Vector2.one);
                m.SetTextureOffset(p, Vector2.zero);
            }
            m.name = "VoxelHoloSight_Body";
            return m;
        }

        private static Material MakeTransparent(Color color, Texture tex, int queue)
        {
            string rp = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.GetType().Name : "";
            if (rp.Contains("HD")) return null; // в HDRP легаси-шейдеров нет
            Shader s = Shader.Find("Sprites/Default");
            if (s == null) return null;
            var m = new Material(s) { color = color, mainTexture = tex, renderQueue = queue };
            return m;
        }

        /// <summary>Пиксельная сетка EOTech: кольцо + точка, без сглаживания — в стиле вокселей.</summary>
        private static Texture2D BuildReticleTexture()
        {
            const int size = 33;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "VoxelHoloReticle",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            int c = size / 2;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                bool ring = d >= 13.5f && d <= 15.5f;
                bool dot = x == c && y == c;
                // Риски внизу/сверху/по бокам кольца, как у EOTech 68 MOA
                bool tick = (x == c && (y <= 3 || y >= size - 4)) || (y == c && (x <= 3 || x >= size - 4));
                tex.SetPixel(x, y, ring || dot || tick ? Color.white : Color.clear);
            }
            tex.Apply(false, false);
            return tex;
        }

        private static void Quad(Transform parent, string name, Vector3 pos, Vector2 size, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // ------------------------------------------------------------------ размер вокселя

        /// <summary>
        /// Размер вокселя оружия в мировых единицах: из сетки вершин меша (если он читаемый),
        /// иначе — по длине оружия (~72 вокселя на винтовку).
        /// </summary>
        public static float DetectWeaponVoxelSize(GameObject weapon, float weaponLength)
        {
            float best = float.MaxValue;
            foreach (MeshFilter mf in weapon.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null || !mesh.isReadable || mesh.vertexCount < 8) continue;
                Vector3[] v = mesh.vertices;
                Vector3 scale = mf.transform.lossyScale;
                for (int axis = 0; axis < 3; axis++)
                {
                    var values = new SortedSet<int>();
                    foreach (Vector3 p in v) values.Add(Mathf.RoundToInt(p[axis] * 10000f));
                    int prev = int.MinValue;
                    foreach (int val in values)
                    {
                        if (prev != int.MinValue)
                        {
                            float diff = (val - prev) / 10000f * Mathf.Abs(scale[axis]);
                            if (diff > 0.0005f && diff < best) best = diff;
                        }
                        prev = val;
                    }
                }
            }

            if (best == float.MaxValue || best > weaponLength / 20f) best = weaponLength / 72f;
            return Mathf.Clamp(best, 0.002f, 0.05f);
        }

        /// <summary>
        /// Воксель прицела: делитель вокселя оружия, чтобы прицел был ~10 см длиной
        /// и при этом сетки совпадали (1:1, 1:2, 1:3...).
        /// </summary>
        public static float SightVoxelSize(float weaponVoxel)
        {
            const float targetLength = 0.1f;
            float ideal = targetLength / L;
            int div = Mathf.Max(1, Mathf.RoundToInt(weaponVoxel / ideal));
            return weaponVoxel / div;
        }
    }
}
