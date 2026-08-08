using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    private void Start()
    {
        if (GetComponentInChildren<Gacha.Presentation.FirstRunContentSetupController>() == null)
        {
            var host = new GameObject("First Run Content Setup");
            host.transform.SetParent(transform, false);
            host.AddComponent<Gacha.Presentation.FirstRunContentSetupController>();
        }
    }

        //root.Q<Button>("btn_language_cn").clicked += () => LocaleSwitcher.SetLocale("zh-CN");
        //root.Q<Button>("btn_language_en").clicked += () => LocaleSwitcher.SetLocale("en-US");
    public void GachaBtnClick()
    {
        GameManager.Instance.loadManager.LoadScene(2);
    }

    public void CollectionBtnClick()
    {
        GameManager.Instance.loadManager.LoadScene(3);
    }

    public void SettingBtnClick()
    {
        GameManager.Instance.loadManager.LoadScene(4);
    }

    public void ContentBtnClick()
    {
        GameManager.Instance.loadManager.LoadScene(5);
    }
}
