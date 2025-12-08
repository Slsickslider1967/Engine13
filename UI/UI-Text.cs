using System.Numerics;
using Engine13.Graphics;
using Engine13.Input;
using Engine13.Utilities;
using Veldrid;

namespace Engine13.UI
{
    public class UIText : UIElement
    {
        public string Text { get; set; }
        public float FontSize { get; set; } = 16f;
        public Vector4 Colour { get; set; } = new Vector4(1f, 1f, 1f, 1f);

        public UIText(Vector2 position, Vector2 size, string text) : base(position, size)
        {
            Text = text;
        }

        public override void Update(InputManager Input)
        {
            // Static text; no update logic needed currently.
        }

        public override void Draw(Renderer renderer)
        {
            if (!Visible) return;

            // Draw text: there's no text pipeline in Renderer yet, so we store text and log for now.
            Console.WriteLine($"UIText: '{Text}' @ {Position}");
        }

        public void SetText(string newText)
        {
            Text = newText;
        }
    }
}