// Signature stub copied from com.unity.inputsystem 1.20.1 (Keyboard.cs, ButtonControl.cs, KeyControl.cs).
namespace UnityEngine.InputSystem.Controls
{
    public class AxisControl { }
    public class ButtonControl : AxisControl
    {
        public bool isPressed => false;
        public bool wasPressedThisFrame => false;
    }
    public class KeyControl : ButtonControl { }
}
namespace UnityEngine.InputSystem
{
    using UnityEngine.InputSystem.Controls;
    public enum Key
    {
        None, Space, Enter, Tab, Backquote, Quote, Semicolon, Comma, Period, Slash, Backslash, LeftBracket, RightBracket, Minus, Equals,
        A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9, Digit0,
        LeftShift, RightShift, LeftAlt, RightAlt, LeftCtrl, RightCtrl, LeftMeta, RightMeta, ContextMenu, Escape, LeftArrow, RightArrow, UpArrow, DownArrow, Backspace,
    }
    public class InputDevice { }
    public class Keyboard : InputDevice
    {
        public static Keyboard current { get; private set; }
        public KeyControl this[Key key] => null;
    }
}
