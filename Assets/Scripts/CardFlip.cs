using UnityEngine;

public class CardFlip : MonoBehaviour
{
    [SerializeField] private float flipDuration = 0.4f; private bool isFront = true;

    public void Flip()
    {
        Quaternion target = isFront
            ? Quaternion.Euler(0, 180, 0)
            : Quaternion.identity;

        LeanTween.rotate(gameObject, target.eulerAngles, flipDuration)
                 .setEase(LeanTweenType.easeInOutQuad);

        isFront = !isFront;
    }

}