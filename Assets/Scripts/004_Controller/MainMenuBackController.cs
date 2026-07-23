using Gacha.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class MainMenuBackController : MonoBehaviour
{
    public void MenuBtnClick()
    {
        UIFeedbackService.Play(FeedbackCue.Back);
        if (GameManager.Instance != null && GameManager.Instance.loadManager != null)
            GameManager.Instance.loadManager.LoadScene(1);
        else
            SceneManager.LoadScene("002_MainMenuScene");
    }
}
