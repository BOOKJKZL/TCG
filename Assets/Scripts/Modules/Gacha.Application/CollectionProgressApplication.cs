using System;

namespace Gacha.Application
{
    public sealed class CollectionItemProgress
    {
        public CollectionItemProgress(string printingId, int ownedCount, bool isNew)
        {
            if (string.IsNullOrWhiteSpace(printingId))
                throw new ArgumentException("A printing id is required.", nameof(printingId));
            if (ownedCount < 0)
                throw new ArgumentOutOfRangeException(nameof(ownedCount));

            PrintingId = printingId.Trim();
            OwnedCount = ownedCount;
            IsNew = ownedCount > 0 && isNew;
        }

        public string PrintingId { get; }
        public int OwnedCount { get; }
        public bool IsOwned => OwnedCount > 0;
        public bool IsNew { get; }
    }

    public interface ICollectionProgressStore
    {
        CollectionItemProgress GetProgress(string printingId);
        bool MarkSeen(string printingId);
    }
}
