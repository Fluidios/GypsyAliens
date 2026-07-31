using GypsyAliens.Network;
using GypsyAliens.Npc;
using UnityEngine;
using UnityEngine.Rendering;

namespace GypsyAliens.Gameplay
{
    /// <summary>
    /// Spawn-point evacuation pad: extracts dragged animals and completes the level
    /// when all animals are stolen and every player stands in the zone.
    /// </summary>
    public sealed class EvacuationZone : MonoBehaviour
    {
        [SerializeField] float _radius = 3.2f;
        [SerializeField] float _saucerAltitude = 11f;
        [SerializeField] float _checkInterval = 0.12f;

        float _checkTimer;
        Transform _saucer;
        Material _ringMat;
        Material _saucerMat;
        Material _beamMat;
        LineRenderer _ringLine;
        GameObject _beam;
        float _pulse;
        Vector3 _saucerHome;

        public float Radius => _radius;

        /// <summary>World Y where an abducted animal should disappear into the saucer.</summary>
        public float SaucerIntakeHeight =>
            _saucer != null ? _saucer.position.y - 0.15f : transform.position.y + _saucerAltitude;

        public void Configure(float radius, float saucerAltitude)
        {
            _radius = Mathf.Max(0.5f, radius);
            _saucerAltitude = Mathf.Max(4f, saucerAltitude);
            BuildVisuals();
        }

        void Update()
        {
            AnimateVisuals();

            _checkTimer -= Time.deltaTime;
            if (_checkTimer > 0f)
            {
                return;
            }

            _checkTimer = _checkInterval;
            TickMission();
        }

        void TickMission()
        {
            var session = NetworkGameSession.Instance;
            if (session == null || !session.HasStateAuthority || session.LevelCompleted)
            {
                return;
            }

            TryExtractAnimals();
            session.TryCompleteLevelIfReady(this);
        }

        void TryExtractAnimals()
        {
            var animals = FindObjectsByType<NetworkFearfulNpc>(FindObjectsSortMode.None);
            for (var i = 0; i < animals.Length; i++)
            {
                var npc = animals[i];
                if (npc == null || !npc.Object || !npc.Object.IsValid)
                {
                    continue;
                }

                if (!npc.IsDragged || npc.IsExtracting)
                {
                    continue;
                }

                if (!ContainsPoint(npc.transform.position))
                {
                    continue;
                }

                npc.BeginExtraction(this);
            }
        }

        public bool ContainsPoint(Vector3 worldPoint)
        {
            var flat = worldPoint - transform.position;
            flat.y = 0f;
            return flat.sqrMagnitude <= _radius * _radius;
        }

        public bool AreAllPlayersInside()
        {
            var players = FindObjectsByType<NetworkPlayerController>(FindObjectsSortMode.None);
            var found = 0;
            for (var i = 0; i < players.Length; i++)
            {
                var p = players[i];
                if (p == null || p.Object == null || !p.Object.IsValid)
                {
                    continue;
                }

                found++;
                if (!ContainsPoint(p.transform.position))
                {
                    return false;
                }
            }

            return found > 0;
        }

        public void PlayExtractionEffect(Vector3 from)
        {
            if (_beam != null)
            {
                var midY = (transform.position.y + SaucerIntakeHeight) * 0.5f;
                var beamHeight = Mathf.Max(0.5f, (SaucerIntakeHeight - transform.position.y) * 0.5f);
                _beam.SetActive(true);
                _beam.transform.position = new Vector3(from.x, midY, from.z);
                _beam.transform.localScale = new Vector3(0.55f, beamHeight, 0.55f);
            }

            if (_saucer != null)
            {
                _saucer.position = new Vector3(from.x, _saucerHome.y, from.z);
            }
        }

        void BuildVisuals()
        {
            var ringGo = new GameObject("EvacRing");
            ringGo.transform.SetParent(transform, false);
            _ringLine = ringGo.AddComponent<LineRenderer>();
            _ringLine.loop = true;
            _ringLine.useWorldSpace = false;
            _ringLine.positionCount = 48;
            _ringLine.startWidth = 0.08f;
            _ringLine.endWidth = 0.08f;
            _ringLine.shadowCastingMode = ShadowCastingMode.Off;
            _ringMat = CreateUnlit(new Color(0.25f, 0.95f, 0.55f, 0.85f));
            _ringLine.sharedMaterial = _ringMat;
            for (var i = 0; i < 48; i++)
            {
                var a = (i / 48f) * Mathf.PI * 2f;
                _ringLine.SetPosition(i, new Vector3(Mathf.Cos(a) * _radius, 0.05f, Mathf.Sin(a) * _radius));
            }

            var saucerGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            saucerGo.name = "Saucer";
            Destroy(saucerGo.GetComponent<Collider>());
            saucerGo.transform.SetParent(transform, false);
            saucerGo.transform.localPosition = new Vector3(0f, _saucerAltitude, 0f);
            saucerGo.transform.localScale = new Vector3(_radius * 0.75f, 0.08f, _radius * 0.75f);
            _saucer = saucerGo.transform;
            _saucerHome = _saucer.position;
            _saucerMat = CreateUnlit(new Color(0.55f, 0.85f, 1f, 0.7f));
            saucerGo.GetComponent<MeshRenderer>().sharedMaterial = _saucerMat;

            _beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _beam.name = "Beam";
            Destroy(_beam.GetComponent<Collider>());
            _beam.transform.SetParent(transform, false);
            _beam.transform.localPosition = new Vector3(0f, _saucerAltitude * 0.5f, 0f);
            _beam.transform.localScale = new Vector3(0.55f, _saucerAltitude * 0.5f, 0.55f);
            _beamMat = CreateUnlit(new Color(0.4f, 1f, 0.7f, 0.22f));
            _beam.GetComponent<MeshRenderer>().sharedMaterial = _beamMat;
            _beam.SetActive(false);
        }

        void AnimateVisuals()
        {
            _pulse += Time.deltaTime;
            var pulse = 0.7f + 0.3f * (0.5f + 0.5f * Mathf.Sin(_pulse * 3.5f));
            if (_ringMat != null)
            {
                var c = new Color(0.25f, 0.95f, 0.55f, 0.55f + 0.35f * pulse);
                ApplyColor(_ringMat, c);
                if (_ringLine != null)
                {
                    _ringLine.startColor = c;
                    _ringLine.endColor = c;
                }
            }

            if (_saucer != null)
            {
                _saucer.Rotate(0f, 35f * Time.deltaTime, 0f, Space.World);
                var bob = _saucerHome.y + Mathf.Sin(_pulse * 1.6f) * 0.2f;
                var p = _saucer.position;
                p.y = bob;
                _saucer.position = p;
            }
        }

        static Material CreateUnlit(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            ApplyColor(mat, color);
            if (mat.HasProperty("_ZWrite"))
            {
                mat.SetFloat("_ZWrite", 0f);
            }

            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
            }

            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return mat;
        }

        static void ApplyColor(Material mat, Color color)
        {
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }
            else if (mat.HasProperty("_Color"))
            {
                mat.color = color;
            }
        }
    }
}
