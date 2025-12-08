using Engine13.Graphics;
using Engine13.Input;
using Veldrid;

namespace Engine13.UI
{
    public class UIManager
    {
        private readonly List<UIElements> _uiElements = new();
        private UIText _statsText;
        private UIButton _pauseButton;
        private UIButton _stepButton;
        private UIButton _resetButton;
        private UIText _speedText;
        private UIButton _speedUpButton;
        private UIButton _speedDownButton;

        public bool IsPaused { get; private set; }
        public bool ShouldStep { get; private set; }
        public bool ShouldReset { get; private set; }
        public float PlaybackSpeed { get; private set; } = 1.0f;

        public void Initialize(GraphicsDevice GD)
        {

        }

        private void ResetSimulation()
        {
            ShouldReset = true;
        }

        public void Update(InputManager input)
        {
            ShouldStep = false;

            foreach (var element in _uiElements)
            {
                element.Update(input);
            }
        }

        public void Draw(Renderer renderer)
        {
            foreach (var element in _uiElements)
            {
                element.Draw(renderer);
            }
        }
    }
}