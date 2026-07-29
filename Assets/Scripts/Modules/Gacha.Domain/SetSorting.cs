using System;
using System.Collections.Generic;

namespace Gacha.Domain
{
    public sealed class SetOrderingMetadata
    {
        public static SetOrderingMetadata Unspecified { get; } = new SetOrderingMetadata();

        public SetOrderingMetadata(
            string setCode = null,
            string eraId = null,
            string generationId = null,
            int? generationOrder = null,
            int? setOrdinal = null)
        {
            if (generationOrder < 0)
                throw new ArgumentOutOfRangeException(nameof(generationOrder));
            if (setOrdinal < 0)
                throw new ArgumentOutOfRangeException(nameof(setOrdinal));

            SetCode = Optional(setCode);
            EraId = Optional(eraId);
            GenerationId = Optional(generationId);
            GenerationOrder = generationOrder;
            SetOrdinal = setOrdinal;
        }

        public string SetCode { get; }
        public string EraId { get; }
        public string GenerationId { get; }
        public int? GenerationOrder { get; }
        public int? SetOrdinal { get; }

        private static string Optional(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    public enum SetSortMode
    {
        Generation = 0,
        ReleaseDate = 1,
        SetCode = 2,
        DisplayName = 3
    }

    public sealed class SetDefinitionComparer : IComparer<SetDefinition>
    {
        private readonly SetSortMode mode;
        private readonly string languageId;
        private readonly string fallbackLanguageId;

        public SetDefinitionComparer(
            SetSortMode mode = SetSortMode.Generation,
            string languageId = "en",
            string fallbackLanguageId = "en")
        {
            if (!Enum.IsDefined(typeof(SetSortMode), mode))
                throw new ArgumentOutOfRangeException(nameof(mode));

            this.mode = mode;
            this.languageId = languageId;
            this.fallbackLanguageId = fallbackLanguageId;
        }

        public int Compare(SetDefinition left, SetDefinition right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return 1;
            if (right == null) return -1;

            int result;
            switch (mode)
            {
                case SetSortMode.Generation:
                    result = CompareGeneration(left, right);
                    break;
                case SetSortMode.ReleaseDate:
                    result = CompareReleaseDate(left, right);
                    break;
                case SetSortMode.SetCode:
                    result = CompareSetCode(left, right);
                    break;
                case SetSortMode.DisplayName:
                    result = CompareDisplayName(left, right);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported set sort mode: {mode}.");
            }

            return result != 0
                ? result
                : StringComparer.Ordinal.Compare(left.Id, right.Id);
        }

        private int CompareGeneration(SetDefinition left, SetDefinition right)
        {
            return FirstDifference(
                CompareNullableNumber(left.Ordering.GenerationOrder, right.Ordering.GenerationOrder),
                CompareOptionalString(left.Ordering.GenerationId, right.Ordering.GenerationId),
                CompareNullableDate(left.ReleaseDate, right.ReleaseDate),
                CompareNullableNumber(left.Ordering.SetOrdinal, right.Ordering.SetOrdinal),
                CompareNaturalCode(left.Ordering.SetCode, right.Ordering.SetCode),
                CompareNames(left, right));
        }

        private int CompareReleaseDate(SetDefinition left, SetDefinition right)
        {
            return FirstDifference(
                CompareNullableDate(left.ReleaseDate, right.ReleaseDate),
                CompareNullableNumber(left.Ordering.GenerationOrder, right.Ordering.GenerationOrder),
                CompareOptionalString(left.Ordering.GenerationId, right.Ordering.GenerationId),
                CompareNullableNumber(left.Ordering.SetOrdinal, right.Ordering.SetOrdinal),
                CompareNaturalCode(left.Ordering.SetCode, right.Ordering.SetCode),
                CompareNames(left, right));
        }

        private int CompareSetCode(SetDefinition left, SetDefinition right)
        {
            return FirstDifference(
                CompareNaturalCode(left.Ordering.SetCode, right.Ordering.SetCode),
                CompareNullableNumber(left.Ordering.GenerationOrder, right.Ordering.GenerationOrder),
                CompareNullableDate(left.ReleaseDate, right.ReleaseDate),
                CompareNullableNumber(left.Ordering.SetOrdinal, right.Ordering.SetOrdinal),
                CompareNames(left, right));
        }

        private int CompareDisplayName(SetDefinition left, SetDefinition right)
        {
            return FirstDifference(
                CompareNames(left, right),
                CompareNullableNumber(left.Ordering.GenerationOrder, right.Ordering.GenerationOrder),
                CompareNullableDate(left.ReleaseDate, right.ReleaseDate),
                CompareNullableNumber(left.Ordering.SetOrdinal, right.Ordering.SetOrdinal),
                CompareNaturalCode(left.Ordering.SetCode, right.Ordering.SetCode));
        }

        private int CompareNames(SetDefinition left, SetDefinition right)
        {
            return StringComparer.OrdinalIgnoreCase.Compare(
                left.GetDisplayName(languageId, fallbackLanguageId),
                right.GetDisplayName(languageId, fallbackLanguageId));
        }

        private static int CompareNullableNumber(int? left, int? right)
        {
            if (left.HasValue && right.HasValue) return left.Value.CompareTo(right.Value);
            if (left.HasValue) return -1;
            if (right.HasValue) return 1;
            return 0;
        }

        private static int CompareNullableDate(DateTime? left, DateTime? right)
        {
            if (left.HasValue && right.HasValue) return left.Value.CompareTo(right.Value);
            if (left.HasValue) return -1;
            if (right.HasValue) return 1;
            return 0;
        }

        private static int CompareOptionalString(string left, string right)
        {
            bool leftMissing = string.IsNullOrWhiteSpace(left);
            bool rightMissing = string.IsNullOrWhiteSpace(right);
            if (leftMissing && rightMissing) return 0;
            if (leftMissing) return 1;
            if (rightMissing) return -1;
            return StringComparer.OrdinalIgnoreCase.Compare(left, right);
        }

        private static int CompareNaturalCode(string left, string right)
        {
            bool leftMissing = string.IsNullOrWhiteSpace(left);
            bool rightMissing = string.IsNullOrWhiteSpace(right);
            if (leftMissing && rightMissing) return 0;
            if (leftMissing) return 1;
            if (rightMissing) return -1;

            int leftIndex = 0;
            int rightIndex = 0;
            while (leftIndex < left.Length && rightIndex < right.Length)
            {
                char leftCharacter = left[leftIndex];
                char rightCharacter = right[rightIndex];
                if (char.IsDigit(leftCharacter) && char.IsDigit(rightCharacter))
                {
                    int leftStart = leftIndex;
                    int rightStart = rightIndex;
                    while (leftIndex < left.Length && left[leftIndex] == '0') leftIndex++;
                    while (rightIndex < right.Length && right[rightIndex] == '0') rightIndex++;
                    int leftSignificantStart = leftIndex;
                    int rightSignificantStart = rightIndex;
                    while (leftIndex < left.Length && char.IsDigit(left[leftIndex])) leftIndex++;
                    while (rightIndex < right.Length && char.IsDigit(right[rightIndex])) rightIndex++;

                    int leftSignificantLength = leftIndex - leftSignificantStart;
                    int rightSignificantLength = rightIndex - rightSignificantStart;
                    int lengthResult = leftSignificantLength.CompareTo(rightSignificantLength);
                    if (lengthResult != 0) return lengthResult;

                    for (int index = 0; index < leftSignificantLength; index++)
                    {
                        int digitResult = left[leftSignificantStart + index]
                            .CompareTo(right[rightSignificantStart + index]);
                        if (digitResult != 0) return digitResult;
                    }

                    int totalLengthResult = (leftIndex - leftStart).CompareTo(rightIndex - rightStart);
                    if (totalLengthResult != 0) return totalLengthResult;
                    continue;
                }

                int characterResult = char.ToUpperInvariant(leftCharacter)
                    .CompareTo(char.ToUpperInvariant(rightCharacter));
                if (characterResult != 0) return characterResult;
                leftIndex++;
                rightIndex++;
            }

            int remainingResult = (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
            return remainingResult != 0
                ? remainingResult
                : StringComparer.Ordinal.Compare(left, right);
        }

        private static int FirstDifference(params int[] comparisons)
        {
            foreach (int comparison in comparisons)
                if (comparison != 0) return comparison;
            return 0;
        }
    }
}
