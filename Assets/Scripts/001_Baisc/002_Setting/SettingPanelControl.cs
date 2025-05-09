using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SettingPanelControl : MonoBehaviour
{
    [Header("Button")]
    public Button displayBtn;
    public Button soundBtn;
    public Button langBtn;
    public Button battleBtn;

    private Button currentBtn;

    [Header("Panel")]
    public GameObject displayPanel;
    public GameObject soundPanel;
    public GameObject langPanel;
    public GameObject battlePanel;

    private GameObject currentPanel;

    [Header("Other")]
    public GameObject leftBtnPanel;
    public GameObject resetPanel;

    private string tempStr = "";

    public static SettingPanelControl Instance { get; private set; }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            //DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnEnable()
    {
        tempStr = "setting_panel_open";
        leftBtnPanel.GetComponent<Animation>().Play(tempStr);
        tempStr = "setting_button_open";
        displayBtn.gameObject.GetComponent<Animation>().Play(tempStr);
        displayPanel.SetActive(true);
        currentBtn = displayBtn;
        currentPanel = displayPanel;
    }

    public void currentBtnClose()
    {
        tempStr = "setting_button_close";
        currentBtn.gameObject.GetComponent<Animation>().Play(tempStr);
        currentPanel.SetActive(false);
    }

    public void displayBtnOnClick()
    {
        if(currentBtn != displayBtn)
        {
            currentBtnClose();
            tempStr = "setting_button_open";
            displayBtn.gameObject.GetComponent<Animation>().Play(tempStr);
            displayPanel.SetActive(true);
            currentBtn = displayBtn;
            currentPanel = displayPanel;
        }
    }

    public void soundBtnOnClick()
    {
        if (currentBtn != soundBtn)
        {
            currentBtnClose();
            tempStr = "setting_button_open";
            soundBtn.gameObject.GetComponent<Animation>().Play(tempStr);
            soundPanel.SetActive(true);
            currentBtn = soundBtn;
            currentPanel = soundPanel;
        }
    }

    public void langBtnOnClick()
    {
        if (currentBtn != langBtn)
        {
            currentBtnClose();
            tempStr = "setting_button_open";
            langBtn.gameObject.GetComponent<Animation>().Play(tempStr);
            langPanel.SetActive(true);
            currentBtn = langBtn;
            currentPanel = langPanel;
        }
    }

    public void battleBtnOnClick()
    {
        if (currentBtn != battleBtn)
        {
            currentBtnClose();
            tempStr = "setting_button_open";
            battleBtn.gameObject.GetComponent<Animation>().Play(tempStr);
            battlePanel.SetActive(true);
            currentBtn = battleBtn;
            currentPanel = battlePanel;
        }
    }

    public void ConfirmResetPanel()
    {
        resetPanel.SetActive(true);
    }

    public void CloseResetPanel()
    {
        resetPanel.SetActive(false);
    }

    public void DisableSettingPage()
    {
        currentBtnClose();
        tempStr = "setting_panel_close";
        leftBtnPanel.GetComponent<Animation>().Play(tempStr);
    }
}
