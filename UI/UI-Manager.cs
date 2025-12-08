using System.Collections.Generic;
using System.Numerics;
using Engine13.Graphics;
using Engine13.Input;
using Engine13.UI;
using Engine13.Utilities;
using Veldrid;

namespace Engine13.UI
{
    public class UIManager
    {
        private readonly List<UIElement> _uiElements = new();
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

        private UIButton _startButton;
        public bool HasStarted { get; private set; } = false;

        public void Initialize(GraphicsDevice GD)
        {
            _startButton = new UIButton(new Vector2(10, 50), new Vector2(100, 30), "Start");
            _startButton.OnClick = () =>
            {
                HasStarted = true;
                // Optionally reset other control flags when starting
                ShouldReset = false;
            };
            _uiElements.Add(_startButton);

            // If you already have Reset button, make sure it clears HasStarted:
            if (_resetButton == null)
            {
                _resetButton = new UIButton(new Vector2(220, 10), new Vector2(80, 30), "Reset");
                _resetButton.OnClick = () => ShouldReset = true;
                _uiElements.Add(_resetButton);
            }
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
