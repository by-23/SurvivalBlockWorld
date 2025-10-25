using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Скрипт для примагничивания элементов в вертикальном ScrollRect
/// </summary>
[RequireComponent(typeof(ScrollRect))]
public class SnappingScroll : MonoBehaviour, IBeginDragHandler, IEndDragHandler, IPointerDownHandler, IPointerUpHandler
{
    private ScrollRect _scrollRect;
    private RectTransform _content;
    private RectTransform _viewport;
    private Transform[] _children;
    private bool _isDragging = false;
    private bool _needsSnapping = false;
    private bool _isPointerDown = false;
    private Coroutine _snapCoroutine;
    private Vector3 _dragStartContentPosition;
    private int _dragStartClosestIndex;

    [Tooltip("Скорость, с которой контент примагничивается к целевому элементу.")]
    public float snapSpeed = 10f;

    [Tooltip("Порог скорости для запуска примагничивания после свайпа.")]
    public float snapStopVelocity = 200f;

    [Header("Логика свайпов")] [Tooltip("Сколько элементов нужно проскроллить, чтобы считать свайп длинным.")]
    public int longSwipeItemsThreshold = 2;

    [Tooltip("Порог скорости для сильного свайпа (мгновенно считаем длинным).")]
    public float strongSwipeVelocity = 2000f;

    void Start()
    {
        _scrollRect = GetComponent<ScrollRect>();
        _content = _scrollRect.content;
        _viewport = _scrollRect.viewport;

        UpdateChildren();
    }

    public void UpdateChildren()
    {
        if (_content == null || _content.childCount == 0)
        {
            _children = new Transform[0];
            return;
        }

        _children = new Transform[_content.childCount];
        for (int i = 0; i < _content.childCount; i++)
        {
            _children[i] = _content.GetChild(i);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _isPointerDown = true;
        if (_snapCoroutine != null)
        {
            StopCoroutine(_snapCoroutine);
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _isPointerDown = false;
        // Если не происходит перетаскивания и требуется примагничивание или скорость мала — запускаем
        if (!_isDragging)
        {
            if (_needsSnapping && _scrollRect.velocity.magnitude < snapStopVelocity)
            {
                FindAndSnapToTarget();
                _needsSnapping = false;
            }
            else if (_scrollRect.velocity.magnitude < snapStopVelocity)
            {
                FindAndSnapToTarget();
            }
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _isDragging = true;
        _needsSnapping = false;
        _dragStartContentPosition = _content.position;
        _dragStartClosestIndex = GetClosestIndex();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _isDragging = false;
        // Если палец все еще зажат — откладываем примагничивание до отпускания
        if (_isPointerDown)
        {
            _needsSnapping = true;
            return;
        }

        // Определяем, был ли свайп длинным/сильным или коротким
        float axisVelocity = _scrollRect.velocity.y;
        float delta = _content.position.y - _dragStartContentPosition.y;

        float spacing = GetItemSpacing();
        float traversedItems = spacing > 0.0001f ? Mathf.Abs(delta) / spacing : 0f;
        bool isStrong = Mathf.Abs(axisVelocity) >= strongSwipeVelocity;
        bool isLong = traversedItems >= longSwipeItemsThreshold;

        if (isStrong || isLong)
        {
            // Длинный/сильный свайп — позволяем инерции замедлиться и потом примагничиваем к ближайшему
            if (_scrollRect.velocity.magnitude < snapStopVelocity)
            {
                FindAndSnapToTarget();
            }
            else
            {
                _needsSnapping = true;
            }
        }
        else
        {
            // Небольшой свайп — переключаемся на соседний элемент по направлению свайпа
            int direction;
            if (Mathf.Abs(delta) < 0.001f)
            {
                // Если почти не сдвинулись, ориентируемся по скорости
                direction = axisVelocity > 0f ? +1 : -1; // положительная скорость = свайп вверх = следующий
            }
            else
            {
                direction = delta > 0f ? +1 : -1; // delta>0 — контент сместился вверх (свайп вниз) => следующий внизу
            }

            int targetIndex = Mathf.Clamp(_dragStartClosestIndex + direction, 0, _children.Length - 1);

            // Останавливаем инерцию и сразу примагничиваемся к выбранному соседу
            _scrollRect.velocity = Vector2.zero;
            SnapToIndex(targetIndex);
        }
    }

    void LateUpdate()
    {
        if (_isDragging) return;
        // Не примагничиваем, пока палец удерживается на экране
        if (_isPointerDown) return;

        if (_needsSnapping && _scrollRect.velocity.magnitude < snapStopVelocity)
        {
            FindAndSnapToTarget();
            _needsSnapping = false;
        }
    }

    private void FindAndSnapToTarget()
    {
        if (_children.Length == 0) return;

        Transform closestChild = null;
        float minDistance = float.MaxValue;

        Vector3 viewportCenter = GetRectCenter(_viewport);

        foreach (Transform child in _children)
        {
            Vector3 childCenter = GetRectCenter(child as RectTransform);
            float distance = Mathf.Abs(childCenter.y - viewportCenter.y);

            if (distance < minDistance)
            {
                minDistance = distance;
                closestChild = child;
            }
        }

        if (closestChild != null)
        {
            Vector3 closestChildCenter = GetRectCenter(closestChild as RectTransform);
            Vector3 childOffset = viewportCenter - closestChildCenter;
            Vector3 targetPosition = _content.position + childOffset;

            targetPosition.x = _content.position.x;

            if (_snapCoroutine != null) StopCoroutine(_snapCoroutine);
            _snapCoroutine = StartCoroutine(SnapToTarget(targetPosition));
        }
    }

    private int GetClosestIndex()
    {
        if (_children == null || _children.Length == 0) return -1;
        Vector3 viewportCenter = GetRectCenter(_viewport);
        int closestIndex = -1;
        float minDistance = float.MaxValue;
        for (int i = 0; i < _children.Length; i++)
        {
            Vector3 childCenter = GetRectCenter(_children[i] as RectTransform);
            float distance = Mathf.Abs(childCenter.y - viewportCenter.y);
            if (distance < minDistance)
            {
                minDistance = distance;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    private float GetItemSpacing()
    {
        if (_children == null || _children.Length < 2) return 0f;
        Vector3 c0 = GetRectCenter(_children[0] as RectTransform);
        Vector3 c1 = GetRectCenter(_children[1] as RectTransform);
        return Mathf.Abs(c1.y - c0.y);
    }

    private void SnapToIndex(int index)
    {
        if (index < 0 || index >= _children.Length) return;
        Transform targetChild = _children[index];
        Vector3 viewportCenter = GetRectCenter(_viewport);
        Vector3 childCenter = GetRectCenter(targetChild as RectTransform);
        Vector3 offset = viewportCenter - childCenter;
        Vector3 targetPosition = _content.position + offset;
        targetPosition.x = _content.position.x;

        if (_snapCoroutine != null) StopCoroutine(_snapCoroutine);
        _snapCoroutine = StartCoroutine(SnapToTarget(targetPosition));
    }

    private Vector3 GetRectCenter(RectTransform rectTransform)
    {
        if (rectTransform == null) return Vector3.zero;

        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);
        // Центр - это среднее арифметическое двух противоположных углов
        return (corners[0] + corners[2]) * 0.5f;
    }

    private IEnumerator SnapToTarget(Vector3 target)
    {
        while (Vector3.Distance(_content.position, target) > 0.01f)
        {
            _content.position = Vector3.Lerp(_content.position, target, Time.deltaTime * snapSpeed);
            yield return null;
        }

        _content.position = target;
        _snapCoroutine = null;
    }
}
