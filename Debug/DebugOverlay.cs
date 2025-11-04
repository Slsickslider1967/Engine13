using System.Numerics;
using Engine13.Graphics;
using Engine13.Utilities.Attributes;

namespace Engine13.Debug
{
    public class DebugOverlay
    {
        private readonly System.Collections.Generic.List<Mesh> _meshes;
        private readonly Renderer _renderer;
        private readonly System.Collections.Generic.Dictionary<Mesh, Vector2> _lastPositions = new System.Collections.Generic.Dictionary<Mesh, Vector2>();

    public float VelocityScale { get; set; } = 1.0f;
    public float MinLineLength { get; set; } = 0.08f; // slightly larger for visibility
    public float LineThickness { get; set; } = 0.03f; // thicker for visibility
    public Vector4 VelocityColor { get; set; } = new Vector4(1f, 0.9f, 0.1f, 1f); // yellow
    public Vector4 EstimatedColor { get; set; } = new Vector4(0.1f, 1f, 1f, 1f); // cyan for estimated
    
        public DebugOverlay(System.Collections.Generic.List<Mesh> meshes, Renderer renderer)
        {
            _meshes = meshes;
            _renderer = renderer;
        }

        public void Draw(float deltaTime)
        {
            for (int i = 0; i < _meshes.Count; i++)
            {
                var m = _meshes[i];
                var oc = m.GetAttribute<ObjectCollision>();

                Vector2 vOc = Vector2.Zero;
                if (oc != null)
                {
                    vOc = oc.Velocity;
                }
                Vector2 vEst = Vector2.Zero;
                bool hasEst = false;
                if (deltaTime > 1e-6f && _lastPositions.TryGetValue(m, out var last))
                {
                    vEst = (m.Position - last) / deltaTime;
                    hasEst = true;
                }

                _lastPositions[m] = m.Position;

                Vector2 v = vOc;
                bool usedEst = false;
                if (hasEst)
                {
                    if (vEst.LengthSquared() > vOc.LengthSquared() * 1.21f) // 10%+ margin squared ~ 1.21
                    {
                        v = vEst;
                        usedEst = true;
                    }
                }

                float s2 = v.LengthSquared();
                Vector2 shown;
                if (s2 < 1e-12f)
                {
                    shown = new Vector2(MinLineLength, 0f);
                }
                else
                {
                    float s = System.MathF.Sqrt(s2);
                    Vector2 dir = v / s;
                    shown = dir * System.MathF.Max(s * VelocityScale, MinLineLength);
                }

                var color = usedEst ? EstimatedColor : VelocityColor;
                _renderer.DrawVelocityVector(m.Position, shown, 1f, color, LineThickness);
            }
        }
    }
}
