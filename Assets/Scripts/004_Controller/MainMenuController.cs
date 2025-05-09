using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
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
}