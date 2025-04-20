using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;

    private void Start()
    {
        var root = uiDocument.rootVisualElement;
        root.Q<Button>("btn_open_gacha").clicked += () => SceneManager.LoadScene("GachaScene");
        root.Q<Button>("btn_open_collection").clicked += () => SceneManager.LoadScene("CollectionScene");
        root.Q<Button>("btn_language_cn").clicked += () => LocaleSwitcher.SetLocale("zh-CN");
        root.Q<Button>("btn_language_en").clicked += () => LocaleSwitcher.SetLocale("en-US");
    }

}