using System;
using UnityEngine;

namespace Gacha.Presentation
{
    public static class HomeSceneRequest
    {
        private enum RequestKind { None, Product, Pokedex, Printing }

        private static RequestKind kind;
        private static string requestedId;
        private static bool prepareProduct;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState() => Clear();

        public static void RequestProduct(string productId, bool prepare)
        {
            requestedId = RequiredId(productId);
            kind = RequestKind.Product;
            prepareProduct = prepare;
        }

        public static void RequestPokedex()
        {
            Clear();
            kind = RequestKind.Pokedex;
        }

        public static void RequestPrinting(string printingId)
        {
            requestedId = RequiredId(printingId);
            kind = RequestKind.Printing;
            prepareProduct = false;
        }

        public static bool TryConsumeProduct(out string productId, out bool prepare)
        {
            productId = null;
            prepare = false;
            if (kind != RequestKind.Product)
                return false;
            productId = requestedId;
            prepare = prepareProduct;
            Clear();
            return true;
        }

        public static bool TryConsumeCollection(out bool openPokedex, out string printingId)
        {
            openPokedex = false;
            printingId = null;
            if (kind != RequestKind.Pokedex && kind != RequestKind.Printing)
                return false;
            openPokedex = kind == RequestKind.Pokedex;
            if (kind == RequestKind.Printing)
                printingId = requestedId;
            Clear();
            return true;
        }

        public static void Clear()
        {
            kind = RequestKind.None;
            requestedId = null;
            prepareProduct = false;
        }

        private static string RequiredId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A content id is required.", nameof(id));
            return id.Trim();
        }
    }
}
