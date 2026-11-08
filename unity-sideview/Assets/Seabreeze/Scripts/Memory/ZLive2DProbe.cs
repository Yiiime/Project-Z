using System;
using System.Linq;
using Live2D.Cubism.Core;
using UnityEngine;

namespace Seabreeze
{
    internal static class ZLive2DProbe
    {
        public static CubismParameter Parameter(CubismModel model, string id)
        {
            return model?.Parameters?.FirstOrDefault(p => p != null && p.Id == id);
        }

        public static bool IsSimulated(string mode)
        {
            return !string.IsNullOrEmpty(mode) && mode.StartsWith("simulated", StringComparison.OrdinalIgnoreCase);
        }

        public static string Argument(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length && !string.IsNullOrWhiteSpace(args[index + 1]) ? args[index + 1] : null;
        }
    }
}
