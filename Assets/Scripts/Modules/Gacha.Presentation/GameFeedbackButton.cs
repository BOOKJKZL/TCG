using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Gacha.Presentation
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class GameFeedbackButton : MonoBehaviour,
        IPointerDownHandler,
        IPointerUpHandler,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerClickHandler,
        ISubmitHandler
    {
        [SerializeField, Range(0.8f, 1f)] private float pressedScale = 0.94f;
        [SerializeField, Range(1f, 1.1f)] private float hoverScale = 1.02f;
        [SerializeField, Range(0.01f, 0.25f)] private float transitionDuration = 0.07f;
        [SerializeField] private FeedbackCue clickCue = FeedbackCue.ButtonClick;

        private Selectable selectable;
        private Vector3 baseScale;
        private Coroutine scaleRoutine;
        private bool pointerInside;

        private void Awake()
        {
            selectable = GetComponent<Selectable>();
            baseScale = transform.localScale;
        }

        private void OnEnable()
        {
            if (baseScale == Vector3.zero)
            {
                baseScale = transform.localScale;
            }
        }

        private void OnDisable()
        {
            StopScaleRoutine();
            transform.localScale = baseScale;
            pointerInside = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (CanInteract())
            {
                AnimateTo(pressedScale);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            AnimateTo(pointerInside ? hoverScale : 1f);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            pointerInside = true;
            if (CanInteract())
            {
                AnimateTo(hoverScale);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            pointerInside = false;
            AnimateTo(1f);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            PlayClickFeedback();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (!CanInteract())
            {
                return;
            }

            PlayClickFeedback();
            StartCoroutine(SubmitPulse());
        }

        private IEnumerator SubmitPulse()
        {
            AnimateTo(pressedScale);
            yield return new WaitForSecondsRealtime(transitionDuration);
            AnimateTo(1f);
        }

        private void PlayClickFeedback()
        {
            if (CanInteract())
            {
                UIFeedbackService.Play(clickCue);
            }
        }

        private bool CanInteract()
        {
            return isActiveAndEnabled && selectable != null && selectable.IsInteractable();
        }

        private void AnimateTo(float scaleMultiplier)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            StopScaleRoutine();
            Vector3 target = UIFeedbackService.ReduceMotion
                ? baseScale
                : baseScale * scaleMultiplier;
            scaleRoutine = StartCoroutine(AnimateScale(target));
        }

        private IEnumerator AnimateScale(Vector3 target)
        {
            Vector3 start = transform.localScale;
            float elapsed = 0f;
            float duration = transitionDuration / UIFeedbackService.AnimationSpeed;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                t = t * t * (3f - 2f * t);
                transform.localScale = Vector3.LerpUnclamped(start, target, t);
                yield return null;
            }

            transform.localScale = target;
            scaleRoutine = null;
        }

        private void StopScaleRoutine()
        {
            if (scaleRoutine == null)
            {
                return;
            }

            StopCoroutine(scaleRoutine);
            scaleRoutine = null;
        }
    }
}
