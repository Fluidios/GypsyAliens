using GypsyAliens.Core;
using UnityEngine;

namespace GypsyAliens.Npc
{
    /// <summary>
    /// Builds a Shadow Tactics-style floor vision cone mesh, clipped by wall raycasts.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class VisionConeView : MonoBehaviour
    {
        [SerializeField] float _range = 8f;
        [SerializeField] float _nearRadius = 2.5f;
        [SerializeField] [Range(10f, 180f)] float _angleDegrees = 70f;
        [SerializeField] int _rayCount = 48;
        [SerializeField] float _floorOffset = 0.05f;
        [SerializeField] float _originHeight = 0.35f;
        [SerializeField] LayerMask _occlusionMask;
        [SerializeField] Material _material;

        static readonly Color DefaultNear = new Color(0.25f, 0.95f, 0.35f, 0.45f);
        static readonly Color DefaultFar = new Color(0.15f, 0.75f, 0.25f, 0.32f);
        static readonly Color AlertNear = new Color(1f, 0.92f, 0.15f, 0.55f);
        static readonly Color AlertFar = new Color(0.95f, 0.75f, 0.05f, 0.4f);

        MeshFilter _filter;
        MeshRenderer _renderer;
        Mesh _mesh;
        Vector3[] _vertices;
        Vector2[] _uvs;
        int[] _triangles;
        Material _runtimeMaterial;
        float _alertFill;

        public float Range => _range;
        public float NearRadius => _nearRadius;
        public float AngleDegrees => _angleDegrees;
        public float OriginHeight => _originHeight;
        public float AlertFill => _alertFill;

        public void Configure(float range, float nearRadius, float angleDegrees)
        {
            _range = Mathf.Max(0.5f, range);
            _nearRadius = Mathf.Clamp(nearRadius, 0.1f, _range);
            _angleDegrees = Mathf.Clamp(angleDegrees, 10f, 180f);
            ApplyMaterialParams();
        }

        /// <summary>
        /// 0 = normal green cone; 0–1 fills yellow outward from the owner toward the rim.
        /// </summary>
        public void SetAlertFill(float fill01)
        {
            _alertFill = Mathf.Clamp01(fill01);
            ApplyMaterialParams();
        }

        void Awake()
        {
            _filter = GetComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();
            if (_occlusionMask.value == 0)
            {
                _occlusionMask = GameLayers.WallMask;
            }

            EnsureMesh();
            EnsureMaterial();
        }

        void OnDestroy()
        {
            if (_mesh != null)
            {
                Destroy(_mesh);
                _mesh = null;
            }

            if (_runtimeMaterial != null)
            {
                Destroy(_runtimeMaterial);
                _runtimeMaterial = null;
            }
        }

        void LateUpdate()
        {
            Rebuild();
        }

        void EnsureMesh()
        {
            if (_mesh != null)
            {
                return;
            }

            _mesh = new Mesh
            {
                name = "VisionConeMesh",
                hideFlags = HideFlags.DontSave,
            };
            _filter.sharedMesh = _mesh;
            AllocateBuffers();
        }

        void EnsureMaterial()
        {
            if (_runtimeMaterial != null)
            {
                return;
            }

            if (_material != null)
            {
                _runtimeMaterial = new Material(_material);
            }
            else
            {
                var shader = Shader.Find("GypsyAliens/VisionCone");
                _runtimeMaterial = shader != null
                    ? new Material(shader)
                    : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            }

            _runtimeMaterial.name = "VisionCone (Instance)";
            _runtimeMaterial.hideFlags = HideFlags.HideAndDontSave;
            _renderer.sharedMaterial = _runtimeMaterial;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            ApplyMaterialParams();
        }

        void ApplyMaterialParams()
        {
            if (_runtimeMaterial == null)
            {
                return;
            }

            if (_runtimeMaterial.HasProperty("_NearRadius"))
            {
                _runtimeMaterial.SetFloat("_NearRadius", _nearRadius);
            }

            if (_runtimeMaterial.HasProperty("_FarRadius"))
            {
                _runtimeMaterial.SetFloat("_FarRadius", _range);
            }

            if (_runtimeMaterial.HasProperty("_AlertFill"))
            {
                _runtimeMaterial.SetFloat("_AlertFill", _alertFill);
            }

            if (_runtimeMaterial.HasProperty("_ColorNear"))
            {
                _runtimeMaterial.SetColor("_ColorNear", DefaultNear);
            }

            if (_runtimeMaterial.HasProperty("_ColorFar"))
            {
                _runtimeMaterial.SetColor("_ColorFar", DefaultFar);
            }

            if (_runtimeMaterial.HasProperty("_AlertColorNear"))
            {
                _runtimeMaterial.SetColor("_AlertColorNear", AlertNear);
            }

            if (_runtimeMaterial.HasProperty("_AlertColorFar"))
            {
                _runtimeMaterial.SetColor("_AlertColorFar", AlertFar);
            }
        }

        void AllocateBuffers()
        {
            var verts = _rayCount + 2;
            _vertices = new Vector3[verts];
            _uvs = new Vector2[verts];
            _triangles = new int[_rayCount * 3];
        }

        public void Rebuild()
        {
            EnsureMesh();
            EnsureMaterial();

            if (_rayCount < 3)
            {
                _rayCount = 3;
            }

            if (_vertices == null || _vertices.Length != _rayCount + 2)
            {
                AllocateBuffers();
            }

            var origin = transform.position;
            origin.y += _originHeight;
            var floorY = transform.position.y + _floorOffset;

            // Keep mesh in local space relative to this transform (parented to NPC).
            _vertices[0] = transform.InverseTransformPoint(new Vector3(transform.position.x, floorY, transform.position.z));
            _uvs[0] = new Vector2(0.5f, 0f);

            var half = _angleDegrees * 0.5f;
            var forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();

            for (var i = 0; i <= _rayCount; i++)
            {
                var t = i / (float)_rayCount;
                var yaw = Mathf.Lerp(-half, half, t);
                var dir = Quaternion.Euler(0f, yaw, 0f) * forward;
                var maxDist = _range;
                if (Physics.Raycast(origin, dir, out var hit, _range, _occlusionMask, QueryTriggerInteraction.Ignore))
                {
                    // Pull back slightly so the mesh doesn't poke through wall faces.
                    maxDist = Mathf.Max(0.05f, hit.distance - 0.08f);
                }

                var world = origin + dir * maxDist;
                world.y = floorY;
                _vertices[i + 1] = transform.InverseTransformPoint(world);
                _uvs[i + 1] = new Vector2(t, maxDist);
            }

            for (var i = 0; i < _rayCount; i++)
            {
                var tri = i * 3;
                _triangles[tri] = 0;
                _triangles[tri + 1] = i + 1;
                _triangles[tri + 2] = i + 2;
            }

            _mesh.Clear();
            _mesh.vertices = _vertices;
            _mesh.uv = _uvs;
            _mesh.triangles = _triangles;
            _mesh.RecalculateBounds();
        }

        /// <summary>
        /// Returns true if a world point lies inside the (occluded) vision cone on XZ.
        /// </summary>
        public bool ContainsPoint(Vector3 worldPoint, float heightForRay = -1f)
        {
            var flatOrigin = transform.position;
            flatOrigin.y = 0f;
            var flatTarget = worldPoint;
            flatTarget.y = 0f;
            var to = flatTarget - flatOrigin;
            var dist = to.magnitude;
            if (dist > _range || dist < 0.01f)
            {
                return false;
            }

            var forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            forward.Normalize();
            var dir = to / dist;
            if (Vector3.Angle(forward, dir) > _angleDegrees * 0.5f)
            {
                return false;
            }

            var rayOrigin = transform.position;
            rayOrigin.y = heightForRay >= 0f ? heightForRay : transform.position.y + _originHeight;
            var rayDir = (worldPoint - rayOrigin);
            var rayDist = rayDir.magnitude;
            if (rayDist < 0.01f)
            {
                return true;
            }

            rayDir /= rayDist;
            if (Physics.Raycast(rayOrigin, rayDir, out var hit, rayDist, _occlusionMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            return true;
        }
    }
}
