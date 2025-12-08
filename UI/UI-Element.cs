using System.Numerics;
using Engine13.Graphics;
using Engine13.Input;
using Vulkan;

namespace Engine13.UI
{
    public abstract class UIElement
    {
        public Vector2 Position { get; set; }
        public Vector2 Size { get; set; }

        public bool Visible { get; set; }
        public bool Enabled { get; set; }

        protected UIElement(Vector2 position, Vector2 size)
        {
            Position = position;
            Size = size;
        }

        public virtual void Update(InputManager input) { }
        public abstract void Draw(Renderer renderer);

        public bool HitTest(Vector2 windowMousePosition)
        {
            if (!Visible)
                return false;
            float x = windowMousePosition.X;
            float y = windowMousePosition.Y;
            return x >= Position.X
                && x <= Position.X + Size.X
                && y >= Position.Y
                && y <= Position.Y + Size.Y;
        }

        protected void DrawRectOutline(Renderer renderer, Vector4 color, float thickness = 2f)
        {
            // Convert pixel coords -> world coords: your project uses normalized world coords.
            // For now draw rectangle as 4 thin "velocity vectors" in world space using approximate mapping.
            // NOTE: Replace this method with actual UI rendering when you add text/quad pipelines.
            var tl = ScreenToWorld(Position);
            var tr = ScreenToWorld(new Vector2(Position.X + Size.X, Position.Y));
            var br = ScreenToWorld(new Vector2(Position.X + Size.X, Position.Y + Size.Y));
            var bl = ScreenToWorld(new Vector2(Position.X, Position.Y + Size.Y));

            renderer.DrawVelocityVector(tl, tr - tl, 1f, color, thickness * 0.0005f);
            renderer.DrawVelocityVector(tr, br - tr, 1f, color, thickness * 0.0005f);
            renderer.DrawVelocityVector(br, bl - br, 1f, color, thickness * 0.0005f);
            renderer.DrawVelocityVector(bl, tl - bl, 1f, color, thickness * 0.0005f);
        }

        protected Vector2 ScreenToWorld(Vector2 screen)
        {
            // Convert from 0..width and 0..height to -1..1-ish world coords
            // Read framebuffer size from Renderer/GD would be better; for now assume 800x600 fallback.
            float w = 800f;
            float h = 600f;
            try
            {
                // If you want, expose framebuffer size to UI or pass it at Draw time.
            }
            catch { }

            float nx = (screen.X / w) * 2f - 1f;
            float ny = 1f - (screen.Y / h) * 2f; // flip Y so top=+1
            // Apply aspect correction similar to Renderer.BeginFrame if needed
            return new Vector2(nx, ny);
        }
    }
}
