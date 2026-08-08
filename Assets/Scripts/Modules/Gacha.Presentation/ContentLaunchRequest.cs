using System;
using UnityEngine;

namespace Gacha.Presentation
{
    public static class ContentLaunchRequest
    {
        private static string recommendedPackageId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            recommendedPackageId = null;
        }

        public static void Recommend(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                throw new ArgumentException("Recommended package id cannot be empty.", nameof(packageId));
            recommendedPackageId = packageId.Trim();
        }

        public static string ConsumeRecommendation()
        {
            string result = recommendedPackageId;
            recommendedPackageId = null;
            return result;
        }

        public static void Clear()
        {
            recommendedPackageId = null;
        }
    }
}
