using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Gacha.Application;
using Gacha.Domain;
using Gacha.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Gacha.Tests.PlayMode
{
    public class CollectionBrowserPlayModeTests
    {
        [UnityTest]
        public IEnumerator CollectionScene_VirtualizesInstalledCardsAndOpensDetails()
        {
            Type controllerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("CollectionViewController"))
                .First(type => type != null);
            var progressStore = new MemoryCollectionProgressStore();
            PropertyInfo storeOverride = controllerType.GetProperty(
                "CollectionProgressStoreOverride",
                BindingFlags.Static | BindingFlags.Public);
            storeOverride.SetValue(null, progressStore);
            var cues = new List<FeedbackCue>();
            UIFeedbackService.FeedbackPlayed += cues.Add;
            try
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("004_CollectionScene", LoadSceneMode.Single);
                yield return load;
                yield return null;

                MonoBehaviour controller = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None)
                    .FirstOrDefault(component => component.GetType() == controllerType);
                Assert.That(controller, Is.Not.Null);

                float deadline = Time.realtimeSinceStartup + 5f;
                while (!(bool)GetProperty(controller, "IsReady") && Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That((bool)GetProperty(controller, "IsReady"), Is.True, GetProperty(controller, "InitializationError") as string);
                Assert.That((int)GetProperty(controller, "InstalledSetCount"), Is.EqualTo(5));

                UIDocument document = controller.GetComponent<UIDocument>();
                Assert.That(document, Is.Not.Null);
                ListView setList = document.rootVisualElement.Q<ListView>("set-list");
                ListView cardList = document.rootVisualElement.Q<ListView>("card-list");
                Assert.That(setList.virtualizationMethod, Is.EqualTo(CollectionVirtualizationMethod.FixedHeight));
                Assert.That(cardList.virtualizationMethod, Is.EqualTo(CollectionVirtualizationMethod.FixedHeight));

                setList.SetSelection(0);
                yield return null;
                Assert.That((int)GetProperty(controller, "CurrentCardCount"), Is.GreaterThan(0));
                Assert.That(cues, Does.Contain(FeedbackCue.Confirm));

                PrintingDefinition[] availableCards = cardList.itemsSource.Cast<PrintingDefinition>().ToArray();
                progressStore.Set(availableCards[0].Id, 2, true);
                progressStore.Set(availableCards[1].Id, 1, false);
                Invoke(controller, "RefreshCollectionProgress");
                yield return null;
                Assert.That((int)GetProperty(controller, "OwnedCardCount"), Is.EqualTo(2));
                Assert.That((int)GetProperty(controller, "NewCardCount"), Is.EqualTo(1));

                Invoke(controller, "SetOwnedOnlyFilter", true);
                yield return null;
                Assert.That((int)GetProperty(controller, "CurrentCardCount"), Is.EqualTo(2));
                Invoke(controller, "SetNewOnlyFilter", true);
                yield return null;
                Assert.That((int)GetProperty(controller, "CurrentCardCount"), Is.EqualTo(1));

                cardList.SetSelection(0);
                yield return new WaitForSecondsRealtime(0.35f);
                VisualElement details = document.rootVisualElement.Q<VisualElement>("details-panel");
                Assert.That(details.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(details.resolvedStyle.opacity, Is.EqualTo(1f).Within(0.05f));
                Assert.That(progressStore.GetProgress(availableCards[0].Id).IsNew, Is.False);
                Assert.That((int)GetProperty(controller, "NewCardCount"), Is.EqualTo(0));
                Assert.That(cues, Does.Contain(FeedbackCue.CardFlip));

                Invoke(controller, "SetNewOnlyFilter", false);
                Invoke(controller, "SetOwnedOnlyFilter", false);
                TextField search = document.rootVisualElement.Q<TextField>("card-search");
                search.value = "definitely-no-matching-card";
                yield return null;
                Assert.That((int)GetProperty(controller, "CurrentCardCount"), Is.EqualTo(0));
                Assert.That(document.rootVisualElement.Q<Label>("filter-empty").resolvedStyle.display,
                    Is.EqualTo(DisplayStyle.Flex));
                search.value = string.Empty;
                yield return null;

                DropdownField rarityFilter = document.rootVisualElement.Q<DropdownField>("rarity-filter");
                Assert.That(rarityFilter.choices.Count, Is.GreaterThan(1));
                rarityFilter.index = 1;
                yield return null;
                Assert.That((int)GetProperty(controller, "CurrentCardCount"), Is.GreaterThan(0));
                Assert.That((int)GetProperty(controller, "CurrentCardCount"),
                    Is.LessThan((int)GetProperty(controller, "CurrentSetTotalCount")));
                rarityFilter.index = 0;
                yield return null;

                deadline = Time.realtimeSinceStartup + 5f;
                while ((int)GetProperty(controller, "CachedTextureCount") == 0 && Time.realtimeSinceStartup < deadline)
                    yield return null;

                int cached = (int)GetProperty(controller, "CachedTextureCount");
                int available = (int)GetProperty(controller, "CurrentCardCount");
                Assert.That(cached, Is.GreaterThan(0));
                Assert.That(cached, Is.LessThanOrEqualTo(32));
                Assert.That(cached, Is.LessThan(available));

                progressStore.Set(availableCards[1].Id, 1, true);
                progressStore.ThrowOnMarkSeen = true;
                Invoke(controller, "RefreshCollectionProgress");
                LogAssert.Expect(LogType.Warning, "Collection viewed-card status could not be saved: disk full");
                int failingIndex = cardList.itemsSource.Cast<PrintingDefinition>()
                    .Select((printing, index) => new { printing, index })
                    .Single(pair => pair.printing.Id == availableCards[1].Id)
                    .index;
                cardList.SetSelection(failingIndex);
                yield return null;
                Assert.That(progressStore.GetProgress(availableCards[1].Id).IsNew, Is.True);
                Assert.That(document.rootVisualElement.Q<Label>("browser-status").ClassListContains("is-error"), Is.True);
                Assert.That(cues, Does.Contain(FeedbackCue.Error));
            }
            finally
            {
                UIFeedbackService.FeedbackPlayed -= cues.Add;
                storeOverride.SetValue(null, null);
            }
        }

        private static object GetProperty(object target, string name)
        {
            return target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);
        }

        private static void Invoke(object target, string name, params object[] arguments)
        {
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public)?.Invoke(target, arguments);
        }

        private sealed class MemoryCollectionProgressStore : ICollectionProgressStore
        {
            private readonly Dictionary<string, CollectionItemProgress> progress =
                new Dictionary<string, CollectionItemProgress>(StringComparer.Ordinal);

            public bool ThrowOnMarkSeen { get; set; }

            public void Set(string printingId, int count, bool isNew)
            {
                progress[printingId] = new CollectionItemProgress(printingId, count, isNew);
            }

            public CollectionItemProgress GetProgress(string printingId)
            {
                return progress.TryGetValue(printingId, out CollectionItemProgress value)
                    ? value
                    : new CollectionItemProgress(printingId, 0, false);
            }

            public bool MarkSeen(string printingId)
            {
                if (ThrowOnMarkSeen)
                    throw new InvalidOperationException("disk full");
                CollectionItemProgress current = GetProgress(printingId);
                if (!current.IsNew)
                    return false;
                Set(printingId, current.OwnedCount, false);
                return true;
            }
        }
    }
}
