namespace Gacha.Presentation
{
    public enum FeedbackCue
    {
        ButtonClick,
        Back,
        Confirm,
        Error,
        DownloadComplete,
        PackOpen,
        CardFlip,
        RareReveal,
        CollectionNew
    }

    public static class FeedbackCueKeys
    {
        public const string ButtonClick = "ui.button.click";
        public const string Back = "ui.back";
        public const string Confirm = "ui.confirm";
        public const string Error = "ui.error";
        public const string DownloadComplete = "download.complete";
        public const string PackOpen = "pack.open";
        public const string CardFlip = "card.flip";
        public const string RareReveal = "card.rare";
        public const string CollectionNew = "collection.new";

        public static string FromCue(FeedbackCue cue)
        {
            switch (cue)
            {
                case FeedbackCue.Back: return Back;
                case FeedbackCue.Confirm: return Confirm;
                case FeedbackCue.Error: return Error;
                case FeedbackCue.DownloadComplete: return DownloadComplete;
                case FeedbackCue.PackOpen: return PackOpen;
                case FeedbackCue.CardFlip: return CardFlip;
                case FeedbackCue.RareReveal: return RareReveal;
                case FeedbackCue.CollectionNew: return CollectionNew;
                default: return ButtonClick;
            }
        }
    }
}
