// using DG.Tweening;
// using System.Collections;
// using UnityEngine;
// using UnityEngine.UI;
//
// public class ButtonAnimation : MonoBehaviour
// {
//     [SerializeField] float _Strenght = 1.0f;
//
//     [SerializeField] Color _PressColor, _DefaultColor;
//     [SerializeField] Image[] _Images;
//     [SerializeField] Transform _Root;
//
//     bool _isClick;
//
//     public void Click()
//     {
//         if (_isClick) return;
//
//         StartCoroutine(UnClick());
//     }
//
//     IEnumerator UnClick()
//     {
//         _isClick = true;
//
//         foreach (var image in _Images)
//         {
//             image.color = _PressColor;
//         }
//
//         _Root.DOShakeScale(0.2f, _Strenght, 10, 90, true, ShakeRandomnessMode.Full).OnComplete(() => FadeOut());
//
//         yield return new WaitForSeconds(0.5f);
//
//         _isClick = false;
//     }
//
//     void FadeOut()
//     {
//         foreach (var image in _Images)
//         {
//             image.color = _DefaultColor;
//         }
//     }
// }
