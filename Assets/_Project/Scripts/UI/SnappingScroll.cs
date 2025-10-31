using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Скрипт для примагничивания элементов в вертикальном ScrollRect
/// </summary>
[RequireComponent(typeof(ScrollRect))]
public class SnappingScroll : MonoBehaviour, IBeginDragHandler, IEndDragHandler, IDragHandler, IPointerDownHandler,
    IPointerUpHandler
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
    private bool _isVertical; // Автоопределение ориентации
    private Vector2 _pointerStart;
    private Vector2 _pointerLast;
    private bool _hasDrag;
    private bool _indexIncreasesWithPositiveAxis = true; // порядок индексов вдоль активной оси
    private int[] _sortedIndices; // индексы детей, отсортированные по активной оси
    private int[] _indexToRank; // преобразование: индекс ребенка -> ранг в отсортированном списке
    private bool _inertiaWasEnabled;
    private bool _isSnapping;
    private int _lastChildCount = -1;
    private bool _rebuildScheduled;

    [Tooltip("Скорость, с которой контент примагничивается к целевому элементу.")]
    public float snapSpeed = 10f;

    [Tooltip("Порог скорости для запуска примагничивания после свайпа.")]
    public float snapStopVelocity = 200f;

    [Header("Логика свайпов")] [Tooltip("Сколько элементов нужно проскроллить, чтобы считать свайп длинным.")]
    public int longSwipeItemsThreshold = 2;

    [Tooltip("Порог скорости для сильного свайпа (мгновенно считаем длинным).")]
    public float strongSwipeVelocity = 2000f;

    [Tooltip(
        "Минимальное смещение указателя по активной оси (в пикселях) для распознавания свайпа на соседний элемент.")]
    public float minSwipePixels = 3f;

    void Start()
    {
        _scrollRect = GetComponent<ScrollRect>();
        _content = _scrollRect.content;
        _viewport = _scrollRect.viewport;

        _isVertical = DetermineOrientation();

        UpdateChildren();
        _lastChildCount = _content != null ? _content.childCount : -1;
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

        // Определяем направление возрастания индексов вдоль активной оси
        if (_children.Length >= 2)
        {
            Vector3 c0 = GetRectCenter(_children[0] as RectTransform);
            Vector3 c1 = GetRectCenter(_children[1] as RectTransform);
            if (_isVertical)
            {
                _indexIncreasesWithPositiveAxis = (c1.y - c0.y) > 0f;
            }
            else
            {
                _indexIncreasesWithPositiveAxis = (c1.x - c0.x) > 0f;
            }
        }

        // Строим устойчивый порядок по активной оси
        _sortedIndices = new int[_children.Length];
        _indexToRank = new int[_children.Length];
        for (int i = 0; i < _children.Length; i++) _sortedIndices[i] = i;
        System.Array.Sort(_sortedIndices, (a, b) =>
        {
            var ca = GetRectCenter(_children[a] as RectTransform);
            var cb = GetRectCenter(_children[b] as RectTransform);
            float va = _isVertical ? ca.y : ca.x;
            float vb = _isVertical ? cb.y : cb.x;
            return va.CompareTo(vb);
        });
        for (int rank = 0; rank < _sortedIndices.Length; rank++)
        {
            _indexToRank[_sortedIndices[rank]] = rank;
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
        if (!_isDragging && !_isSnapping)
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
        if (_snapCoroutine != null) StopCoroutine(_snapCoroutine);
        _dragStartContentPosition = _content.position;
        _dragStartClosestIndex = GetClosestIndex();
        _pointerStart = eventData.position;
        _pointerLast = eventData.position;
        _hasDrag = true;
        _inertiaWasEnabled = _scrollRect.inertia;
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

        // Определяем режим: сильная прокрутка (инерционный флинг) или свайп на 1 элемент
        float axisVelocity = _isVertical ? _scrollRect.velocity.y : _scrollRect.velocity.x;
        float delta = _isVertical
            ? _content.position.y - _dragStartContentPosition.y
            : _content.position.x - _dragStartContentPosition.x;

        bool isStrong = Mathf.Abs(axisVelocity) >= strongSwipeVelocity;

        if (isStrong)
        {
            // Сильная прокрутка — дожидаемся замедления и притягиваем к ближайшему к центру
            if (_scrollRect.velocity.magnitude < snapStopVelocity)
            {
                FindAndSnapToTarget();
            }
            else
            {
                _needsSnapping = true;
            }

            return;
        }

        // Свайп — строго на один соседний элемент по направлению свайпа (на экране)
        const float tiny = 0.001f;
        if (Mathf.Abs(delta) < tiny && Mathf.Abs(axisVelocity) < tiny && !_hasDrag)
        {
            // Нет явного свайпа — просто притягиваем к ближайшему
            FindAndSnapToTarget();
            return;
        }

        // Направление свайпа в экранных координатах (по началу/концу)
        float pointerDeltaAxis = _isVertical
            ? (eventData.position.y - _pointerStart.y)
            : (eventData.position.x - _pointerStart.x);
        int swipeAxisDir = pointerDeltaAxis > 0f ? +1 : (pointerDeltaAxis < 0f ? -1 : 0);
        // Порог по пикселям для явного свайпа
        if (Mathf.Abs(pointerDeltaAxis) < minSwipePixels)
        {
            swipeAxisDir = 0;
        }

        if (swipeAxisDir == 0)
        {
            // если не хватило данных по указателю, ориентируемся по скорости, затем по сдвигу контента
            swipeAxisDir = Mathf.Abs(axisVelocity) >= tiny ? (axisVelocity > 0f ? +1 : -1) : (delta > 0f ? +1 : -1);
        }

        // Используем ранги в устойчивом порядке по активной оси
        int currentIndex = GetClosestIndex();
        if (currentIndex < 0)
        {
            FindAndSnapToTarget();
            return;
        }

        int currentRank = _indexToRank != null && currentIndex < _indexToRank.Length
            ? _indexToRank[currentIndex]
            : currentIndex;
        int targetRank = Mathf.Clamp(currentRank + (swipeAxisDir > 0 ? +1 : -1), 0, _children.Length - 1);
        int targetIndex = (_sortedIndices != null && targetRank < _sortedIndices.Length)
            ? _sortedIndices[targetRank]
            : targetRank;

        // Останавливаем инерцию и сразу примагничиваемся к выбранному соседу
        _scrollRect.velocity = Vector2.zero;
        _scrollRect.inertia = false;
        _needsSnapping = false;
        SnapToIndex(targetIndex);
        _hasDrag = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        _pointerLast = eventData.position;
    }

    void LateUpdate()
    {
        if (_isDragging) return;
        // Не примагничиваем, пока палец удерживается на экране
        if (_isPointerDown) return;

        // Автообновление при добавлении/удалении элементов в контенте
        if (_content != null)
        {
            int current = _content.childCount;
            if (current != _lastChildCount)
            {
                ScheduleRebuild();
                _lastChildCount = current;
            }
        }

        if (!_isSnapping && _needsSnapping && _scrollRect.velocity.magnitude < snapStopVelocity)
        {
            FindAndSnapToTarget();
            _needsSnapping = false;
        }
    }

    public void RequestRebuild()
    {
        ScheduleRebuild();
    }

    private void ScheduleRebuild()
    {
        if (_rebuildScheduled) return;
        _rebuildScheduled = true;
        StartCoroutine(RebuildAfterLayout());
    }

    private IEnumerator RebuildAfterLayout()
    {
        // Ждём завершения лайаута текущего кадра
        yield return null;
        Canvas.ForceUpdateCanvases();
        _isVertical = DetermineOrientation();
        UpdateChildren();
        _rebuildScheduled = false;
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
            float distance = _isVertical
                ? Mathf.Abs(childCenter.y - viewportCenter.y)
                : Mathf.Abs(childCenter.x - viewportCenter.x);

            if (distance < minDistance)
            {
                minDistance = distance;
                closestChild = child;
            }
        }

        if (closestChild != null)
        {
            _scrollRect.velocity = Vector2.zero;
            _scrollRect.inertia = false;
            int index = GetClosestIndex();
            SnapToIndex(index);
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
            float distance = _isVertical
                ? Mathf.Abs(childCenter.y - viewportCenter.y)
                : Mathf.Abs(childCenter.x - viewportCenter.x);
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
        return _isVertical ? Mathf.Abs(c1.y - c0.y) : Mathf.Abs(c1.x - c0.x);
    }

    private void SnapToIndex(int index)
    {
        if (index < 0 || index >= _children.Length) return;
        Transform targetChild = _children[index];
        Vector3 viewportCenterWorld = GetRectCenter(_viewport);
        Vector3 childCenterWorld = GetRectCenter(targetChild as RectTransform);
        Vector2 viewportCenterLocal = _viewport.InverseTransformPoint(viewportCenterWorld);
        Vector2 childCenterLocal = _viewport.InverseTransformPoint(childCenterWorld);
        Vector2 deltaInViewport = viewportCenterLocal - childCenterLocal;

        Vector2 targetAnchored = _content.anchoredPosition;
        if (_isVertical)
        {
            targetAnchored.y -= deltaInViewport.y;
        }
        else
        {
            targetAnchored.x -= deltaInViewport.x;
        }

        if (_snapCoroutine != null) StopCoroutine(_snapCoroutine);
        _isSnapping = true;
        _snapCoroutine = StartCoroutine(SnapAnchoredToTarget(targetAnchored));
    }

    private bool DetermineOrientation()
    {
        // Если задан только один флаг — используем его
        if (_scrollRect.horizontal && !_scrollRect.vertical) return false; // горизонтальный
        if (_scrollRect.vertical && !_scrollRect.horizontal) return true; // вертикальный

        // Если оба разрешены или оба запрещены — определяем по размеру контента относительно вьюпорта
        var contentRect = _content != null ? _content.rect : new Rect();
        var viewportRect = _viewport != null ? _viewport.rect : new Rect();
        float overflowX = Mathf.Max(0f, contentRect.width - viewportRect.width);
        float overflowY = Mathf.Max(0f, contentRect.height - viewportRect.height);
        if (Mathf.Approximately(overflowX, overflowY))
        {
            // дефолт — вертикальный
            return true;
        }

        return overflowY >= overflowX; // если вертикального overflow больше — вертикальный, иначе горизонтальный
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
        // Возвращаем исходное состояние инерции
        _scrollRect.inertia = _inertiaWasEnabled;
        _isSnapping = false;
    }

    private IEnumerator SnapAnchoredToTarget(Vector2 target)
    {
        _scrollRect.StopMovement();
        while (Vector2.Distance(_content.anchoredPosition, target) > 0.01f)
        {
            _content.anchoredPosition = Vector2.Lerp(_content.anchoredPosition, target, Time.deltaTime * snapSpeed);
            yield return null;
        }

        _content.anchoredPosition = target;
        _snapCoroutine = null;
        _scrollRect.inertia = _inertiaWasEnabled;
        _isSnapping = false;
    }
}
