using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Gacha.Tests.PlayMode
{
    public sealed class ResolutionManagerPlayModeTests
    {
        [UnityTest]
        public IEnumerator ManagerNormalizesBothRenderCallbacksAndUnsubscribesOnDestruction()
        {
            Type managerType = Type.GetType("ResolutionManager, Gacha.Runtime.Foundation", true);
            FieldInfo srpEvent = typeof(RenderPipelineManager).GetField("beginCameraRendering",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(srpEvent, Is.Not.Null);
            Delegate[] Callbacks(Delegate renderer) => renderer?.GetInvocationList()
                .Where(callback => callback.Method.DeclaringType == managerType).ToArray() ?? Array.Empty<Delegate>();
            Delegate[] Srp() => Callbacks((Delegate)srpEvent.GetValue(null));
            Delegate[] BuiltIn() => Callbacks(Camera.onPreCull);
            int srpBefore = Srp().Length;
            int builtInBefore = BuiltIn().Length;
            var manager = new GameObject("resolution lifecycle test");
            var cameraHost = new GameObject("resolution callback camera");
            try
            {
                Camera camera = cameraHost.AddComponent<Camera>();
                camera.enabled = false;
                manager.AddComponent(managerType);
                Assert.That(BuiltIn().Length, Is.EqualTo(builtInBefore + 1));
                Assert.That(Srp().Length, Is.EqualTo(srpBefore + 1));
                var custom = new Rect(0.1f, 0.2f, 0.75f, 0.6f);
                var full = new Rect(0f, 0f, 1f, 1f);
                camera.rect = custom;
                ((Camera.CameraCallback)BuiltIn().Last())(camera);
                Assert.That(camera.rect, Is.EqualTo(full));
                camera.rect = custom;
                ((Action<ScriptableRenderContext, Camera>)Srp().Last())(default, camera);
                Assert.That(camera.rect, Is.EqualTo(full));
                UnityEngine.Object.Destroy(manager);
                yield return null;
                Assert.That(Srp().Length, Is.EqualTo(srpBefore));
                Assert.That(BuiltIn().Length, Is.EqualTo(builtInBefore));
            }
            finally
            {
                if (manager != null) UnityEngine.Object.Destroy(manager);
                UnityEngine.Object.Destroy(cameraHost);
            }
        }
    }
}
