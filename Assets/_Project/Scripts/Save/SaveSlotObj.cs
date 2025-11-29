using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SaveSlotObj : MonoBehaviour
{
    public Image _iconImg;

    public void SetIcon(Sprite icon)
    {
        if (icon == null) return;

        _iconImg.sprite = icon;
    }

    public void SetColor(Color cr)
    {
        if (_iconImg == null) return;

        _iconImg.color = cr;
    }

    public Color GetColor()
    {
        return _iconImg.color;
    }
}
