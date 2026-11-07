using UnityEngine;
using UnityEngine.Rendering;

namespace Handover.Unity
{
    /// <summary>
    /// Greybox helpers: primitives without colliders (so they never intercept hand UI rays),
    /// tinted with property blocks on the render pipeline's default material, plus one cached
    /// transparent material for water, dry areas and canopies. Placeholder art only.
    /// </summary>
    public static class Greybox
    {
        public static readonly Color Wood = new Color(0.62f, 0.45f, 0.29f);
        public static readonly Color Stone = new Color(0.55f, 0.56f, 0.55f);
        public static readonly Color Amber = new Color(0.95f, 0.62f, 0.12f);
        public static readonly Color Harbour = new Color(0.16f, 0.45f, 0.38f);
        public static readonly Color Deep = new Color(0.02f, 0.27f, 0.24f);
        public static readonly Color Water = new Color(0.20f, 0.55f, 0.58f, 0.45f);
        public static readonly Color KeeperColour = new Color(0.93f, 0.58f, 0.13f);
        public static readonly Color PipColour = new Color(0.84f, 0.18f, 0.16f);
        public static readonly Color LumaColour = new Color(0.20f, 0.42f, 0.85f);
        public static readonly Color Peg = new Color(0.86f, 0.72f, 0.52f);

        private static MaterialPropertyBlock _block;
        private static Material _transparent;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public static GameObject Primitive(PrimitiveType type, Transform parent, string name, Color color,
            Vector3 localPosition, Vector3 localScale, bool transparent = false)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(collider);
                }
                else
                {
                    Object.DestroyImmediate(collider);
                }
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            Renderer renderer = go.GetComponent<Renderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (transparent)
            {
                Material material = TransparentMaterial();
                if (material != null)
                {
                    renderer.sharedMaterial = material;
                }
            }

            Tint(renderer, color);
            return go;
        }

        public static void Tint(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }

            renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColor, color);
            _block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(_block);
        }

        public static void Tint(GameObject go, Color color)
        {
            if (go != null)
            {
                Tint(go.GetComponent<Renderer>(), color);
            }
        }

        /// <summary>URP Unlit set up for alpha blending; null when no suitable shader is available.</summary>
        public static Material TransparentMaterial()
        {
            if (_transparent != null)
            {
                return _transparent;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                return null;
            }

            var m = new Material(shader) { name = "Handover Greybox Transparent" };
            if (m.HasProperty("_Surface"))
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }

            m.renderQueue = (int)RenderQueue.Transparent;
            _transparent = m;
            return m;
        }

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
