using UnityEngine;

public class StartController : MonoBehaviour
{
    public void StartBtnClick()
    {
        GameManager.Instance.loadManager.LoadScene(1);
    }
}