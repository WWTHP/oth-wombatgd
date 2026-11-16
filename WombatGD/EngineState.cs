using Il2Cpp;

namespace WombatGD
{
    /// <summary>
    /// Reads RPM and throttle from the parameters the game feeds the engine's
    /// FMOD event, so the mod tracks exactly what the stock engine sound hears.
    /// </summary>
    internal static class EngineState
    {
        public static void Read(CarAudioManager cam, out float rpm, out float throttle)
        {
            rpm = 0f;
            throttle = 0f;
            var em = cam?.engineAudioEmitter;
            if (em == null) return;
            var inst = em.EventInstance;
            if (!inst.isValid()) return;
            inst.getParameterByName("rpmEngine", out rpm);
            inst.getParameterByName("throttle", out throttle);
            throttle = UnityEngine.Mathf.Clamp01(throttle);
        }
    }
}
