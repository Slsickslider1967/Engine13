using System;
using System.Numerics;
using Engine13.Graphics;
using Engine13.Utilities;
using Engine13.Utilities.Attributes;

namespace Engine13.Debug
{
    public class DebugOverlay
    {
        private readonly System.Collections.Generic.List<Entity> _entities;
        private readonly Renderer _renderer;
        private readonly System.Collections.Generic.Dictionary<Entity, Vector2> _lastPositions =
            new System.Collections.Generic.Dictionary<Entity, Vector2>();
        private System.Collections.Generic.List<CollisionInfo> _collisions =
            new System.Collections.Generic.List<CollisionInfo>();

        public bool ShowSatDebug { get; set; } = true;
        public float SatNormalLength { get; set; } = 0.2f;
        public float SatLineThickness { get; set; } = 0.02f;
        public Vector4 SatNormalColour { get; set; } = new Vector4(0.1f, 0.6f, 1f, 1f);
        public Vector4 SatPenetrationColour { get; set; } = new Vector4(1f, 0.2f, 0.2f, 1f);
        public Vector4 SatContactColour { get; set; } = new Vector4(1f, 1f, 1f, 1f);

        public bool ShowOutlines { get; set; } = true;
        public bool ShowAabbs { get; set; } = true;
        public float OutlineThickness { get; set; } = 0.01f;
        public float AabbThickness { get; set; } = 0.008f;
        public Vector4 OutlineAColour { get; set; } = new Vector4(0.2f, 1f, 0.2f, 1f);
        public Vector4 OutlineBColour { get; set; } = new Vector4(1f, 0.2f, 1f, 1f);
        public Vector4 AabbAColour { get; set; } = new Vector4(0.2f, 0.6f, 0.2f, 1f);
        public Vector4 AabbBColour { get; set; } = new Vector4(0.6f, 0.2f, 0.6f, 1f);

        public DebugOverlay(System.Collections.Generic.List<Entity> entities, Renderer renderer)
        {
            _entities = entities;
            _renderer = renderer;
        }

        public void SetCollisions(System.Collections.Generic.List<CollisionInfo> collisions)
        {
            _collisions = collisions ?? new System.Collections.Generic.List<CollisionInfo>();
        }

        public void Draw(float deltaTime)
        {
            // Draw SAT debug visualization for current collisions
            if (ShowSatDebug && _collisions != null)
            {
                for (int i = 0; i < _collisions.Count; i++)
                {
                    var c = _collisions[i];
                    var a = c.EntityA;
                    var b = c.EntityB;
                    if (ShowOutlines)
                    {
                        DrawEntityOutline(a, OutlineAColour, OutlineThickness);
                        DrawEntityOutline(b, OutlineBColour, OutlineThickness);
                    }
                    if (ShowAabbs)
                    {
                        DrawAabb(a.GetAABB(), AabbAColour, AabbThickness);
                        DrawAabb(b.GetAABB(), AabbBColour, AabbThickness);
                    }
                    Vector2 cp = c.ContactPoint;
                    Vector2 n = c.SeparationDirection;
                    Vector2 pen = c.PenetrationDepth; // already normal * overlap

                    // Draw penetration vector (from contact point)
                    _renderer.DrawVelocityVector(
                        cp,
                        pen,
                        1f,
                        SatPenetrationColor,
                        SatLineThickness
                    );

                    // Draw normal direction (fixed length)
                    _renderer.DrawVelocityVector(
                        cp,
                        n * SatNormalLength,
                        1f,
                        SatNormalColor,
                        SatLineThickness
                    );
                    // Arrowhead at the tip of the normal
                    Vector2 tip = cp + n * SatNormalLength;
                    Vector2 tPerp = new Vector2(-n.Y, n.X);
                    float head = SatNormalLength * 0.25f;
                    DrawLine(
                        tip,
                        tip - n * head + tPerp * (head * 0.5f),
                        SatNormalColor,
                        SatLineThickness * 0.8f
                    );
                    DrawLine(
                        tip,
                        tip - n * head - tPerp * (head * 0.5f),
                        SatNormalColor,
                        SatLineThickness * 0.8f
                    );

                    // Draw a small cross at contact point
                    float crossHalf = 0.03f;
                    _renderer.DrawVelocityVector(
                        new Vector2(cp.X - crossHalf, cp.Y),
                        new Vector2(crossHalf * 2f, 0f),
                        1f,
                        SatContactColor,
                        SatLineThickness
                    );
                    _renderer.DrawVelocityVector(
                        new Vector2(cp.X, cp.Y - crossHalf),
                        new Vector2(0f, crossHalf * 2f),
                        1f,
                        SatContactColor,
                        SatLineThickness
                    );
                }
            }
        }

        private void DrawLine(Vector2 start, Vector2 end, Vector4 colour, float thickness)
        {
            _renderer.DrawVelocityVector(start, end - start, 1f, colour, thickness);
        }

        private void DrawAabb(Engine13.Utilities.AABB aabb, Vector4 colour, float thickness)
        {
            Vector2 min = aabb.Min;
            Vector2 max = aabb.Max;
            Vector2 v0 = min;
            Vector2 v1 = new Vector2(max.X, min.Y);
            Vector2 v2 = max;
            Vector2 v3 = new Vector2(min.X, max.Y);
            DrawLine(v0, v1, colour, thickness);
            DrawLine(v1, v2, colour, thickness);
            DrawLine(v2, v3, colour, thickness);
            DrawLine(v3, v0, colour, thickness);
        }

        private void DrawEntityOutline(Entity entity, Vector4 colour, float thickness)
        {
            if (entity == null)
                return;
            var verts = entity.GetVertices();
            if (verts == null || verts.Length < 2)
                return;

            var world = new Vector2[verts.Length];
            int count = VertexCollisionSolver.Instance.CopyWorldSpaceVertices(
                entity,
                world.AsSpan()
            );
            if (count < 2)
                return;

            for (int i = 0; i < count; i++)
            {
                Vector2 a = world[i];
                Vector2 b = world[(i + 1) % count];
                DrawLine(a, b, colour, thickness);
            }
        }
    }
}
