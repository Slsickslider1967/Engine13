using System.ComponentModel;
using System.Numerics;
using Engine13.Utilities;

namespace Engine13.UI
{
    public class UIButton : UIElement
    {
        public string Text { get; set; }
        public Action OnClick { get; set; }

        public bool IsHovered { get; private set;}
        public bool IsPressed { get; private set; }

        public UIButton(Vector2 position, Vector2 size, string text) : base(position, size)
        {
            Text = text;
        }
    }
}