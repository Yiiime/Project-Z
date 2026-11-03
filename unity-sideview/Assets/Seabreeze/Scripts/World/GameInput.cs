using UnityEngine;
using UnityEngine.InputSystem;

namespace Seabreeze
{
    public static class GameInput
    {
        public static float Horizontal
        {
            get
            {
                var k = Keyboard.current;
                if (k == null)
                    return 0;
                return ((k.dKey.isPressed || k.rightArrowKey.isPressed) ? 1 : 0) - ((k.aKey.isPressed || k.leftArrowKey.isPressed) ? 1 : 0);
            }
        }

        public static bool JumpPressed => Pressed(Key.Space) || Pressed(Key.W) || Pressed(Key.UpArrow);
        public static bool InteractPressed => Pressed(Key.E);
        public static bool Pressed(Key key) => Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;
    }
}
