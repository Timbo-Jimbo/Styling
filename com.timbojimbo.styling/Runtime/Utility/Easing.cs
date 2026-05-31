using UnityEngine;

namespace TimboJimbo.Styling
{
    public enum EaseType
    {
        Linear = 0,
        InSine, OutSine, InOutSine,
        InQuad, OutQuad, InOutQuad,
        InCubic, OutCubic, InOutCubic,
        InQuart, OutQuart, InOutQuart,
        InQuint, OutQuint, InOutQuint,
        InExpo, OutExpo, InOutExpo,
        InCirc, OutCirc, InOutCirc,
        InBack, OutBack, InOutBack,
        InElastic, OutElastic, InOutElastic,
        InBounce, OutBounce, InOutBounce,
    }

    public static class EaseUtility
    {
        public static float Evaluate(float t, EaseType type) => type switch
        {
            EaseType.Linear => t,
            EaseType.InSine => InSine(t),
            EaseType.OutSine => OutSine(t),
            EaseType.InOutSine => InOutSine(t),
            EaseType.InQuad => InQuad(t),
            EaseType.OutQuad => OutQuad(t),
            EaseType.InOutQuad => InOutQuad(t),
            EaseType.InCubic => InCubic(t),
            EaseType.OutCubic => OutCubic(t),
            EaseType.InOutCubic => InOutCubic(t),
            EaseType.InQuart => InQuart(t),
            EaseType.OutQuart => OutQuart(t),
            EaseType.InOutQuart => InOutQuart(t),
            EaseType.InQuint => InQuint(t),
            EaseType.OutQuint => OutQuint(t),
            EaseType.InOutQuint => InOutQuint(t),
            EaseType.InExpo => InExpo(t),
            EaseType.OutExpo => OutExpo(t),
            EaseType.InOutExpo => InOutExpo(t),
            EaseType.InCirc => InCirc(t),
            EaseType.OutCirc => OutCirc(t),
            EaseType.InOutCirc => InOutCirc(t),
            EaseType.InBack => InBack(t),
            EaseType.OutBack => OutBack(t),
            EaseType.InOutBack => InOutBack(t),
            EaseType.InElastic => InElastic(t),
            EaseType.OutElastic => OutElastic(t),
            EaseType.InOutElastic => InOutElastic(t),
            EaseType.InBounce => InBounce(t),
            EaseType.OutBounce => OutBounce(t),
            EaseType.InOutBounce => InOutBounce(t),
            _ => t,
        };

        // --- Sine ---
        public static float InSine(float t) => 1f - Mathf.Cos((t * Mathf.PI) / 2f);
        public static float OutSine(float t) => Mathf.Sin((t * Mathf.PI) / 2f);
        public static float InOutSine(float t) => -(Mathf.Cos(Mathf.PI * t) - 1f) / 2f;

        // --- Quad ---
        public static float InQuad(float t) => t * t;
        public static float OutQuad(float t) => 1f - (1f - t) * (1f - t);
        public static float InOutQuad(float t) => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;

        // --- Cubic ---
        public static float InCubic(float t) => t * t * t;
        public static float OutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
        public static float InOutCubic(float t) => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;

        // --- Quart ---
        public static float InQuart(float t) => t * t * t * t;
        public static float OutQuart(float t) => 1f - Mathf.Pow(1f - t, 4f);
        public static float InOutQuart(float t) => t < 0.5f ? 8f * t * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 4f) / 2f;

        // --- Quint ---
        public static float InQuint(float t) => t * t * t * t * t;
        public static float OutQuint(float t) => 1f - Mathf.Pow(1f - t, 5f);
        public static float InOutQuint(float t) => t < 0.5f ? 16f * t * t * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 5f) / 2f;

        // --- Expo ---
        public static float InExpo(float t) => t == 0f ? 0f : Mathf.Pow(2f, 10f * t - 10f);
        public static float OutExpo(float t) => t == 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
        public static float InOutExpo(float t) => t == 0f ? 0f : t == 1f ? 1f : t < 0.5f ? Mathf.Pow(2f, 20f * t - 10f) / 2f : (2f - Mathf.Pow(2f, -20f * t + 10f)) / 2f;

        // --- Circ ---
        public static float InCirc(float t) => 1f - Mathf.Sqrt(1f - Mathf.Pow(t, 2f));
        public static float OutCirc(float t) => Mathf.Sqrt(1f - Mathf.Pow(t - 1f, 2f));
        public static float InOutCirc(float t) => t < 0.5f ? (1f - Mathf.Sqrt(1f - Mathf.Pow(2f * t, 2f))) / 2f : (Mathf.Sqrt(1f - Mathf.Pow(-2f * t + 2f, 2f)) + 1f) / 2f;

        // --- Back ---
        private const float c1 = 1.70158f;
        private const float c2 = c1 * 1.525f;
        private const float c3 = c1 + 1f;

        public static float InBack(float t) => c3 * t * t * t - c1 * t * t;
        public static float OutBack(float t) => 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        public static float InOutBack(float t) => t < 0.5f ? (Mathf.Pow(2f * t, 2f) * ((c2 + 1f) * 2f * t - c2)) / 2f : (Mathf.Pow(2f * t - 2f, 2f) * ((c2 + 1f) * (t * 2f - 2f) + c2) + 2f) / 2f;

        // --- Elastic ---
        private const float c4 = (2f * Mathf.PI) / 3f;
        private const float c5 = (2f * Mathf.PI) / 4.5f;

        public static float InElastic(float t) => t == 0f ? 0f : t == 1f ? 1f : -Mathf.Pow(2f, 10f * t - 10f) * Mathf.Sin((t * 10f - 10.75f) * c4);
        public static float OutElastic(float t) => t == 0f ? 0f : t == 1f ? 1f : Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
        public static float InOutElastic(float t) => t == 0f ? 0f : t == 1f ? 1f : t < 0.5f ? -(Mathf.Pow(2f, 20f * t - 10f) * Mathf.Sin((20f * t - 11.125f) * c5)) / 2f : (Mathf.Pow(2f, -20f * t + 10f) * Mathf.Sin((20f * t - 11.125f) * c5)) / 2f + 1f;

        // --- Bounce ---
        private const float n1 = 7.5625f;
        private const float d1 = 2.75f;

        public static float OutBounce(float t)
        {
            if (t < 1f / d1) {
                return n1 * t * t;
            } else if (t < 2f / d1) {
                return n1 * (t -= 1.5f / d1) * t + 0.75f;
            } else if (t < 2.5f / d1) {
                return n1 * (t -= 2.25f / d1) * t + 0.9375f;
            } else {
                return n1 * (t -= 2.625f / d1) * t + 0.984375f;
            }
        }

        public static float InBounce(float t) => 1f - OutBounce(1f - t);
        public static float InOutBounce(float t) => t < 0.5f ? (1f - OutBounce(1f - 2f * t)) / 2f : (1f + OutBounce(2f * t - 1f)) / 2f;
    }
}