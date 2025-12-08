using System.ComponentModel;
using System.Numerics;
using Engine13.Graphics;
using Engine13.Input;
using Engine13.Utilities;
using Veldrid;

namespace Engine13.UI
{
    public class UIButton : UIElement
    {
        public string Text { get; set; }
        public Action OnClick { get; set; }

        public bool IsHovered { get; private set; }
        public bool IsPressed { get; private set; }

        public Vector4 BackgroundColour { get; set; } = new Vector4(0.15f, 0.15f, 0.15f, 1f);
        public Vector4 HoverColour { get; set; } = new Vector4(0.25f, 0.25f, 0.25f, 1f);
        public Vector4 PressedColour { get; set; } = new Vector4(0.3f, 0.55f, 0.9f, 1f);
        public Vector4 OutlineColour { get; set; } = new Vector4(1f, 1f, 1f, 1f);

        public UIButton(Vector2 position, Vector2 size, string text)
            : base(position, size)
        {
            Text = text;
        }

        public override void Update(InputManager Input)
        {
            if (!Enabled || !Visible)
                return;

            var MP = Input.MousePosition;
            bool Hit = HitTest(MP);
            IsHovered = Hit;

            if (Input.WasMouseButtonPressed(MouseButton.Left) && Hit)
            {
                IsPressed = true;
                OnClick?.Invoke();
            }
            else if (Input.WasMouseButtonReleased(MouseButton.Left))
            {
                IsPressed = false;
            }
            else if (!Input.IsMouseButtonDown(MouseButton.Left))
            {
                IsPressed = false;
            }
        }

        public override void Draw(Renderer renderer)
        {
            if (!Visible)
                return;

            Vector4 color = BackgroundColour;
            if (IsPressed)
                color = PressedColour;
            else if (IsHovered)
                color = HoverColour;

            // Draw outline and background as rectangle edges (placeholder)
            DrawRectOutline(renderer, OutlineColour, thickness: 2f);
            // Optionally fill using many lines or add a proper quad pipeline later.

            // Draw text: there's no text pipeline in Renderer yet, so we store text and log for now.
            // Replace this with a TextRenderer when available.
            // Example: Debug output (useful during development)
            System.Console.WriteLine(
                $"UI Button draw: '{Text}' at {Position} size {Size} hovered={IsHovered} pressed={IsPressed}"
            );
        }
    }
}
