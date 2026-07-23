using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
            AsyncOperation load = SceneManager.LoadSceneAsync("004_CollectionScene", LoadSceneMode.Single);
            yield return load;
            yield return null;

            MonoBehaviour controller = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .FirstOrDefault(component => component.GetType().Name == "CollectionViewController");
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

            var cues = new List<FeedbackCue>();
            UIFeedbackService.FeedbackPlayed += cues.Add;
            try
            {
                setList.SetSelection(0);
                yield return null;
                Assert.That((int)GetProperty(controller, "CurrentCardCount"), Is.GreaterThan(0));
                Assert.That(cues, Does.Contain(FeedbackCue.Confirm));

                deadline = Time.realtimeSinceStartup + 5f;
                while ((int)GetProperty(controller, "CachedTextureCount") == 0 && Time.realtimeSinceStartup < deadline)
                    yield return null;

                int cached = (int)GetProperty(controller, "CachedTextureCount");
                int available = (int)GetProperty(controller, "CurrentCardCount");
                Assert.That(cached, Is.GreaterThan(0));
                Assert.That(cached, Is.LessThanOrEqualTo(32));
                Assert.That(cached, Is.LessThan(available));

                cardList.SetSelection(0);
                yield return new WaitForSecondsRealtime(0.35f);
                VisualElement details = document.rootVisualElement.Q<VisualElement>("details-panel");
                Assert.That(details.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(details.resolvedStyle.opacity, Is.EqualTo(1f).Within(0.05f));
                Assert.That(cues, Does.Contain(FeedbackCue.CardFlip));
            }
            finally
            {
                UIFeedbackService.FeedbackPlayed -= cues.Add;
            }
        }

        private static object GetProperty(object target, string name)
        {
            return target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);
        }
    }
}
