using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LoadingScreen : MonoBehaviour
{
    public static LoadingScreen instance;

    [Space]
    [Header("References")]
    [SerializeField] GameObject _LoadingPanel;
    [SerializeField] Image _PreviewImg;
    [SerializeField] TextMeshProUGUI _MapNameTxt;

    private void Awake()
    {
        if(instance == null)
            instance = this;

        DontDestroyOnLoad(gameObject);

        View(false, null, "");
    }

    public void View(bool active, Sprite icon, string mapName)
    {
        if (active) StartCoroutine(ViewDelay(active, icon, mapName, 0.0f));
        else StartCoroutine(ViewDelay(active, icon, mapName, 2.0f));
    }

    IEnumerator ViewDelay(bool active, Sprite icon, string mapName, float delayTime)
    {
        yield return new WaitForSeconds(delayTime);

        _LoadingPanel.SetActive(active);
        _MapNameTxt.text = mapName;

        _PreviewImg.enabled = icon != null;
        _PreviewImg.sprite = icon;
    }
}
