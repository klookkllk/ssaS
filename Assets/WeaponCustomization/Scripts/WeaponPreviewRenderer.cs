using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeaponCustomization
{
    /// <summary>
    /// Рендерит вращающуюся 3D-копию оружия в RenderTexture для меню.
    /// Копия собирается только из мешей и материалов (без скриптов KINEMATION),
    /// живёт на отдельном слое вдали от сцены и видна только камере превью.
    /// </summary>
    public class WeaponPreviewRenderer : MonoBehaviour
    {
        public float autoRotateSpeed = 20f;
        public float dragSensitivity = 0.35f;
        public Vector3 initialEuler = new Vector3(8f, 90f, 0f);

        private static readonly Vector3 RigPosition = new Vector3(0f, -5000f, 0f);

        private int _layer;
        private Camera _camera;
        private Light _light;
        private Transform _pivot;
        private Transform _model;
        private RenderTexture _texture;
        private readonly List<Mesh> _bakedMeshes = new List<Mesh>();
        private float _idleTimer;

        public RenderTexture Texture => _texture;

        public void Init(int layer, int width, int height)
        {
            _layer = layer;
            transform.position = RigPosition;

            _texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "WeaponPreviewRT", antiAliasing = 4 };
            _texture.Create();

            _pivot = new GameObject("PreviewPivot").transform;
            _pivot.SetParent(transform, false);

            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(transform, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.cullingMask = 1 << layer;
            _camera.fieldOfView = 25f;
            _camera.nearClipPlane = 0.01f;
            _camera.farClipPlane = 50f;
            _camera.targetTexture = _texture;
            _camera.enabled = false;

            var lightGo = new GameObject("PreviewLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Directional;
            _light.intensity = 1.3f;
            _light.cullingMask = 1 << layer;
            _light.shadows = LightShadows.None;

            // Остальные камеры не должны видеть слой превью.
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam != _camera) cam.cullingMask &= ~(1 << layer);
            }
        }

        public void SetVisible(bool visible)
        {
            if (_camera != null) _camera.enabled = visible;
            if (_light != null) _light.enabled = visible;
            if (_pivot != null) _pivot.gameObject.SetActive(visible);
        }

        /// <summary>Пересобрать превью по текущему виду оружия (вместе с обвесом).</summary>
        public void Show(GameObject source)
        {
            Quaternion prevRotation = _model != null ? _pivot.localRotation : Quaternion.Euler(initialEuler);
            Clear();
            if (source == null) return;

            _model = new GameObject("PreviewModel").transform;
            _model.SetParent(_pivot, false);
            CopyVisuals(source.transform, _model);

            _pivot.localRotation = prevRotation;
            Frame();
        }

        public void Drag(Vector2 delta)
        {
            if (_pivot == null) return;
            _pivot.Rotate(Vector3.up, -delta.x * dragSensitivity, Space.World);
            _pivot.Rotate(Vector3.right, delta.y * dragSensitivity, Space.World);
            _idleTimer = 2f;
        }

        private void Update()
        {
            if (_pivot == null || _camera == null || !_camera.enabled) return;
            // unscaledDeltaTime — чтобы вращалось и при паузе (timeScale = 0).
            if (_idleTimer > 0f) { _idleTimer -= Time.unscaledDeltaTime; return; }
            _pivot.Rotate(Vector3.up, autoRotateSpeed * Time.unscaledDeltaTime, Space.World);
        }

        private void CopyVisuals(Transform sourceRoot, Transform targetRoot)
        {
            // Всё копируется в мировых единицах относительно корня оружия (без масштаба корня).
            Quaternion invRootRot = Quaternion.Inverse(sourceRoot.rotation);

            foreach (Renderer r in sourceRoot.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled) continue;

                Mesh mesh = null;
                if (r is MeshRenderer)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null) mesh = mf.sharedMesh;
                }
                else if (r is SkinnedMeshRenderer smr)
                {
                    mesh = new Mesh { name = smr.name + "_Baked" };
                    smr.BakeMesh(mesh);
                    _bakedMeshes.Add(mesh);
                }
                if (mesh == null) continue;

                Transform t = r.transform;
                var go = new GameObject(t.name) { layer = _layer };
                go.transform.SetParent(targetRoot, false);
                go.transform.localPosition = invRootRot * (t.position - sourceRoot.position);
                go.transform.localRotation = invRootRot * t.rotation;
                // BakeMesh уже учитывает масштаб, поэтому для скиннутых мешей — единичный.
                go.transform.localScale = r is SkinnedMeshRenderer ? Vector3.one : t.lossyScale;

                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = r.sharedMaterials;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        private void Frame()
        {
            Renderer[] renderers = _model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            Quaternion rot = _pivot.localRotation;
            _pivot.localRotation = Quaternion.identity;

            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);

            // Центрируем модель на пивоте.
            _model.position += _pivot.position - b.center;
            _pivot.localRotation = rot;

            float radius = b.extents.magnitude;
            float dist = radius / Mathf.Sin(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
            _camera.transform.position = _pivot.position - Vector3.forward * dist;
            _camera.transform.rotation = Quaternion.identity;
            _camera.farClipPlane = dist + radius * 2f + 1f;
        }

        private void Clear()
        {
            if (_model != null) Destroy(_model.gameObject);
            _model = null;
            foreach (Mesh m in _bakedMeshes) Destroy(m);
            _bakedMeshes.Clear();
        }

        private void OnDestroy()
        {
            Clear();
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
        }
    }
}
