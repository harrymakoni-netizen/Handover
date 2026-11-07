using UnityEngine;

namespace Handover.Unity
{
    /// <summary>Keys used by the Editor keyboard driver and the greybox shortcuts.</summary>
    public enum DriverKey
    {
        W, A, S, D, Q, E, Z, X, C, F, G, H, R, V,
        Tab, Shift, Space, Enter, Backspace, Escape,
        Digit1, Digit2, Digit3, Digit4, Digit0,
        P, M, B, N, K, L, T, Y, U, I, O,
    }

    /// <summary>
    /// Reads the keyboard through the Input System when it is active, otherwise through the
    /// legacy Input Manager. Desktop testing only; nothing here is needed on the headset.
    /// </summary>
    public static class DriverInput
    {
        public static bool Held(DriverKey key)
        {
#if ENABLE_INPUT_SYSTEM
            UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                return keyboard[ToInputSystem(key)].isPressed;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(ToLegacy(key));
#else
            return false;
#endif
        }

        public static bool Pressed(DriverKey key)
        {
#if ENABLE_INPUT_SYSTEM
            UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                return keyboard[ToInputSystem(key)].wasPressedThisFrame;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(ToLegacy(key));
#else
            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static UnityEngine.InputSystem.Key ToInputSystem(DriverKey key)
        {
            switch (key)
            {
                case DriverKey.W: return UnityEngine.InputSystem.Key.W;
                case DriverKey.A: return UnityEngine.InputSystem.Key.A;
                case DriverKey.S: return UnityEngine.InputSystem.Key.S;
                case DriverKey.D: return UnityEngine.InputSystem.Key.D;
                case DriverKey.Q: return UnityEngine.InputSystem.Key.Q;
                case DriverKey.E: return UnityEngine.InputSystem.Key.E;
                case DriverKey.Z: return UnityEngine.InputSystem.Key.Z;
                case DriverKey.X: return UnityEngine.InputSystem.Key.X;
                case DriverKey.C: return UnityEngine.InputSystem.Key.C;
                case DriverKey.F: return UnityEngine.InputSystem.Key.F;
                case DriverKey.G: return UnityEngine.InputSystem.Key.G;
                case DriverKey.H: return UnityEngine.InputSystem.Key.H;
                case DriverKey.R: return UnityEngine.InputSystem.Key.R;
                case DriverKey.V: return UnityEngine.InputSystem.Key.V;
                case DriverKey.Tab: return UnityEngine.InputSystem.Key.Tab;
                case DriverKey.Shift: return UnityEngine.InputSystem.Key.LeftShift;
                case DriverKey.Space: return UnityEngine.InputSystem.Key.Space;
                case DriverKey.Enter: return UnityEngine.InputSystem.Key.Enter;
                case DriverKey.Backspace: return UnityEngine.InputSystem.Key.Backspace;
                case DriverKey.Escape: return UnityEngine.InputSystem.Key.Escape;
                case DriverKey.Digit1: return UnityEngine.InputSystem.Key.Digit1;
                case DriverKey.Digit2: return UnityEngine.InputSystem.Key.Digit2;
                case DriverKey.Digit3: return UnityEngine.InputSystem.Key.Digit3;
                case DriverKey.Digit4: return UnityEngine.InputSystem.Key.Digit4;
                case DriverKey.Digit0: return UnityEngine.InputSystem.Key.Digit0;
                case DriverKey.P: return UnityEngine.InputSystem.Key.P;
                case DriverKey.M: return UnityEngine.InputSystem.Key.M;
                case DriverKey.B: return UnityEngine.InputSystem.Key.B;
                case DriverKey.N: return UnityEngine.InputSystem.Key.N;
                case DriverKey.K: return UnityEngine.InputSystem.Key.K;
                case DriverKey.L: return UnityEngine.InputSystem.Key.L;
                case DriverKey.T: return UnityEngine.InputSystem.Key.T;
                case DriverKey.Y: return UnityEngine.InputSystem.Key.Y;
                case DriverKey.U: return UnityEngine.InputSystem.Key.U;
                case DriverKey.I: return UnityEngine.InputSystem.Key.I;
                case DriverKey.O: return UnityEngine.InputSystem.Key.O;
                default: return UnityEngine.InputSystem.Key.None;
            }
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        private static KeyCode ToLegacy(DriverKey key)
        {
            switch (key)
            {
                case DriverKey.W: return KeyCode.W;
                case DriverKey.A: return KeyCode.A;
                case DriverKey.S: return KeyCode.S;
                case DriverKey.D: return KeyCode.D;
                case DriverKey.Q: return KeyCode.Q;
                case DriverKey.E: return KeyCode.E;
                case DriverKey.Z: return KeyCode.Z;
                case DriverKey.X: return KeyCode.X;
                case DriverKey.C: return KeyCode.C;
                case DriverKey.F: return KeyCode.F;
                case DriverKey.G: return KeyCode.G;
                case DriverKey.H: return KeyCode.H;
                case DriverKey.R: return KeyCode.R;
                case DriverKey.V: return KeyCode.V;
                case DriverKey.Tab: return KeyCode.Tab;
                case DriverKey.Shift: return KeyCode.LeftShift;
                case DriverKey.Space: return KeyCode.Space;
                case DriverKey.Enter: return KeyCode.Return;
                case DriverKey.Backspace: return KeyCode.Backspace;
                case DriverKey.Escape: return KeyCode.Escape;
                case DriverKey.Digit1: return KeyCode.Alpha1;
                case DriverKey.Digit2: return KeyCode.Alpha2;
                case DriverKey.Digit3: return KeyCode.Alpha3;
                case DriverKey.Digit4: return KeyCode.Alpha4;
                case DriverKey.Digit0: return KeyCode.Alpha0;
                case DriverKey.P: return KeyCode.P;
                case DriverKey.M: return KeyCode.M;
                case DriverKey.B: return KeyCode.B;
                case DriverKey.N: return KeyCode.N;
                case DriverKey.K: return KeyCode.K;
                case DriverKey.L: return KeyCode.L;
                case DriverKey.T: return KeyCode.T;
                case DriverKey.Y: return KeyCode.Y;
                case DriverKey.U: return KeyCode.U;
                case DriverKey.I: return KeyCode.I;
                case DriverKey.O: return KeyCode.O;
                default: return KeyCode.None;
            }
        }
#endif
    }
}
