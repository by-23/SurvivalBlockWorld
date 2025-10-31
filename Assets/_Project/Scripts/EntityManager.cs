using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Assets._Project.Scripts.UI;

[Serializable]
public class EntitySaveData
{
    public int entityId;
    public Vector3 position;
    public Vector3 scale;
    public Quaternion rotation;
    public CubeData[] cubesData;
    public string screenshotPath; // Путь к скриншоту (может быть пустым, если не создан)
}

/// <summary>
/// Управляет сохранением и загрузкой объектов Entity
/// </summary>
public class EntityManager : MonoBehaviour
{
    [Header("UI Buttons")] [SerializeField]
    private UnityEngine.UI.Button _saveButton;

    [SerializeField] private UnityEngine.UI.Button _spawnButton;

    // Спавн обрабатывается через ghost в EntityCreator

    [SerializeField] private EntitySelector _entitySelector;
    private List<EntitySaveData> _savedEntities;
    private int _currentSelectedSaveIndex;

    [Serializable]
    private class SaveFile
    {
        public List<EntitySaveData> entities = new List<EntitySaveData>();
    }

    private string SavesDirectoryPath => Application.persistentDataPath;
    private string SavesFilePath => Path.Combine(Application.persistentDataPath, "entities.json");

    [Header("Screenshot Settings")] [SerializeField]
    private Camera _screenshotCamera;

    [SerializeField] private Vector3 _cameraOffset = new Vector3(0f, 2f, -4f);
    [SerializeField] private bool _useObjectSpaceOffset = true;
    [SerializeField] private int _screenshotWidth = 512;
    [SerializeField] private int _screenshotHeight = 512;
    [SerializeField] private float _framingPadding = 1.1f;

    [Header("Screenshot Isolation")] [SerializeField]
    private int _screenshotSubjectLayer = 30;

    [SerializeField] private Color _screenshotBackgroundColor = new Color(0f, 0f, 0f, 0f);

    [Header("Saved Object UI")] [SerializeField]
    private Transform _savedObjectsContainer;

    [SerializeField] private GameObject _savedObjectItemPrefab;

    [Header("Notifications")] [SerializeField]
    private TextMeshProUGUI _notificationText;

    [Header("Entity Creator")] [SerializeField]
    private MonoBehaviour _entityCreatorBehaviour; // Избегает жесткой зависимости от namespace скрипта

    [Header("Ground Placement")] [SerializeField]
    private LayerMask _groundLayerMask = 1;

    [SerializeField] private float _groundCheckDistance = 100f;

    private void Awake()
    {
        _entitySelector = FindAnyObjectByType<EntitySelector>();
        _savedEntities = new List<EntitySaveData>();

        if (_saveButton != null)
        {
            _saveButton.onClick.AddListener(OnSaveButtonClicked);
        }

        if (_spawnButton != null)
        {
            _spawnButton.onClick.AddListener(OnSpawnButtonClicked);
        }

        if (_screenshotCamera == null)
        {
            var tagged = GameObject.FindWithTag("ScreenshotCamera");
            if (tagged != null)
            {
                tagged.TryGetComponent(out _screenshotCamera);
                if (_screenshotCamera != null)
                {
                    _screenshotCamera.gameObject.SetActive(false);
                }
            }
        }

        // EntityCreator опционален и находится динамически при необходимости

        // Загружаем сущности из постоянной памяти
        LoadAllFromDisk();
        // Восстанавливаем UI элементы для загруженных сохранений
        for (int i = 0; i < _savedEntities.Count; i++)
        {
            string shotPath = _savedEntities[i].screenshotPath;
            if (!string.IsNullOrEmpty(shotPath) && File.Exists(shotPath))
            {
                TryCreateSavedObjectUIItem(shotPath, i);
            }
        }
    }

    private void OnSaveButtonClicked()
    {
        if (_entitySelector == null)
        {
            Debug.LogError("EntitySelector not found!");
            return;
        }

        Entity hoveredEntity = _entitySelector.GetHoveredEntity();

        if (hoveredEntity == null)
        {
            Debug.LogWarning("No entity is being hovered over!");
            return;
        }

        SaveEntity(hoveredEntity);
    }

    private void OnSpawnButtonClicked()
    {
        if (_savedEntities.Count == 0)
        {
            Debug.LogWarning("No saved entities to spawn!");
            return;
        }

        // Проверяем текущий индекс
        if (_currentSelectedSaveIndex < 0 || _currentSelectedSaveIndex >= _savedEntities.Count)
        {
            Debug.LogWarning($"Invalid current selected index {_currentSelectedSaveIndex}, resetting to 0");
            _currentSelectedSaveIndex = 0;
        }

        // Спавним через EntityCreator в текущей позиции/повороте ghost
        InvokeEntityCreator(_currentSelectedSaveIndex);
    }

    /// <summary>
    /// Сохраняет указанную сущность в список сохраненных сущностей
    /// </summary>
    public void SaveEntity(Entity entity)
    {
        if (entity == null)
        {
            Debug.LogError("Cannot save null entity!");
            return;
        }

        // Проверка на дубликат ID сущности
        if (_savedEntities != null && _savedEntities.Exists(s => s.entityId == entity.EntityId))
        {
            ShowNotification(
                $"Объект с ID {entity.EntityId} уже сохранён. Удалите существующий, чтобы сохранить новый.");
            return;
        }

        // Убираем временную подсветку перед сохранением/созданием скриншота
        var outline = entity.GetComponent<EntityOutlineHighlight>();
        if (outline != null)
        {
            outline.HideOutline();
        }

        EntitySaveData saveData = new EntitySaveData
        {
            entityId = entity.EntityId,
            position = entity.transform.position,
            scale = entity.transform.localScale,
            rotation = entity.transform.rotation,
            cubesData = entity.GetSaveData()
        };

        _savedEntities.Add(saveData);
        _currentSelectedSaveIndex = _savedEntities.Count - 1;
        string screenshotPath = TakeEntityScreenshot(entity);
        if (!string.IsNullOrEmpty(screenshotPath))
        {
            saveData.screenshotPath = screenshotPath;
            TryCreateSavedObjectUIItem(screenshotPath, _currentSelectedSaveIndex);
        }

        // Сохраняем все на диск
        SaveAllToDisk();
    }

    private void ShowNotification(string message)
    {
        if (_notificationText != null)
        {
            _notificationText.gameObject.SetActive(true);
            _notificationText.text = message;
            StopAllCoroutines();
            StartCoroutine(HideNotificationAfterSeconds(2f));
        }
        else
        {
            Debug.LogWarning(message);
        }
    }

    private System.Collections.IEnumerator HideNotificationAfterSeconds(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (_notificationText != null)
        {
            _notificationText.gameObject.SetActive(false);
        }
    }


    /// <summary>
    /// Спавнит сохраненную сущность в указанной мировой позиции с заданным поворотом (используется EntityCreator)
    /// </summary>
    public void SpawnSavedEntityAt(int index, Vector3 position, Quaternion rotation)
    {
        if (_savedEntities == null || _savedEntities.Count == 0)
        {
            Debug.LogWarning("No saved entities to spawn!");
            return;
        }

        if (index < 0 || index >= _savedEntities.Count)
        {
            Debug.LogWarning($"Invalid saved entity index: {index} (total: {_savedEntities.Count})");
            return;
        }

        EntitySaveData saveData = _savedEntities[index];

        // Автоматически корректируем позицию так, чтобы объект стоял на земле
        Vector3 correctedPosition = AdjustPositionToGround(position, rotation, saveData);

        // Создаем Entity через фабрику
        Entity newEntity = EntityFactory.CreateEntity(
            correctedPosition,
            rotation,
            saveData.scale,
            isKinematic: true,
            entityName: $"Entity_{DateTime.Now.Ticks}"
        );

        StartCoroutine(LoadEntityAsync(newEntity, saveData, rotation));
    }

    /// <summary>
    /// Корректирует позицию спавна так, чтобы нижняя часть объекта находилась на уровне земли
    /// </summary>
    private Vector3 AdjustPositionToGround(Vector3 spawnPosition, Quaternion rotation, EntitySaveData saveData)
    {
        if (saveData.cubesData == null || saveData.cubesData.Length == 0)
        {
            return spawnPosition;
        }

        // Размер куба (стандартный размер в игре)
        const float cubeSize = 1f;
        const float cubeHalfSize = cubeSize * 0.5f;

        // Вычисляем нижнюю границу объекта из исходных данных
        // Находим минимальную Y координату нижней границы всех кубов (в мировых координатах оригинального объекта)
        float minBottomWorldY = float.MaxValue;
        foreach (var cubeData in saveData.cubesData)
        {
            // Позиция куба в мировых координатах оригинального объекта
            Vector3 cubeWorldPos = cubeData.Position;

            // Нижняя граница куба (куб размером 1, центр в позиции куба, нижняя точка на 0.5 ниже)
            float cubeBottomY = cubeWorldPos.y - cubeHalfSize;

            if (cubeBottomY < minBottomWorldY)
            {
                minBottomWorldY = cubeBottomY;
            }
        }

        // Если не нашли нижнюю границу, возвращаем исходную позицию
        if (minBottomWorldY == float.MaxValue)
        {
            return spawnPosition;
        }

        // Вычисляем смещение нижней границы относительно центра оригинальной Entity
        // Это смещение в мировых координатах
        float bottomOffsetFromOriginalCenter = minBottomWorldY - saveData.position.y;

        // Вычисляем мировую позицию нижней точки нового объекта
        // Учитываем, что spawnPosition - это центр нового Entity
        // Смещение применяем напрямую, так как при спавне используется тот же масштаб
        Vector3 bottomWorldPosition = spawnPosition;
        bottomWorldPosition.y += bottomOffsetFromOriginalCenter;

        // Выполняем Raycast вниз от нижней точки для поиска земли
        RaycastHit hit;
        Vector3 rayStart = bottomWorldPosition;
        rayStart.y += 2f; // Небольшой отступ вверх для начала raycast (чтобы не начинать изнутри земли)

        if (Physics.Raycast(rayStart, Vector3.down, out hit, _groundCheckDistance, _groundLayerMask))
        {
            // Нашли землю - корректируем позицию так, чтобы нижняя точка была на уровне земли
            float groundLevel = hit.point.y;
            float currentBottomY = bottomWorldPosition.y;
            float heightAdjustment = groundLevel - currentBottomY;

            // Корректируем позицию спавна (центр Entity)
            Vector3 correctedPosition = spawnPosition;
            correctedPosition.y += heightAdjustment;

            return correctedPosition;
        }

        // Если не нашли землю, возвращаем исходную позицию
        return spawnPosition;
    }


    /// <summary>
    /// Загружает данные сущности асинхронно
    /// </summary>
    private System.Collections.IEnumerator LoadEntityAsync(Entity entity, EntitySaveData saveData,
        Quaternion newRotation)
    {
        if (saveData.cubesData == null || saveData.cubesData.Length == 0)
        {
            Debug.LogWarning("No cube data to load!");
            yield break;
        }

        CubeSpawner spawner = SaveSystem.Instance?.CubeSpawner;
        if (spawner == null)
        {
            spawner = FindAnyObjectByType<CubeSpawner>();
        }

        if (spawner == null)
        {
            Debug.LogError("CubeSpawner not found!");
            yield break;
        }

        // Извлекаем только поворот по оси Y из нового поворота
        float newYRotation = newRotation.eulerAngles.y;
        float originalYRotation = saveData.rotation.eulerAngles.y;
        float yRotationDelta = newYRotation - originalYRotation;
        Quaternion yRotationDeltaQuat = Quaternion.Euler(0f, yRotationDelta, 0f);

        Vector3 originalEntityCenter = saveData.position;
        Vector3 originalEntityScale = saveData.scale;
        Vector3 newEntityPosition = entity.transform.position;
        float newEntityScale = entity.transform.localScale.x;

        float finalYRotation = newRotation.eulerAngles.y;
        Quaternion finalRotationQuat = Quaternion.Euler(0f, finalYRotation, 0f);

        // Устанавливаем поворот сущности перед расчетом позиций кубов
        entity.transform.rotation = finalRotationQuat;

        CubeData[] adjustedCubeData = new CubeData[saveData.cubesData.Length];
        for (int i = 0; i < saveData.cubesData.Length; i++)
        {
            CubeData original = saveData.cubesData[i];

            // Получаем локальную позицию куба относительно оригинальной сущности (без поворота)
            Vector3 originalLocalPos =
                (original.Position - originalEntityCenter) / Mathf.Max(0.0001f, originalEntityScale.x);

            // Применяем поворот по оси Y для получения желаемой локальной позиции
            Vector3 rotatedLocalPos = yRotationDeltaQuat * originalLocalPos;

            // Конвертируем повернутую локальную позицию в мировую
            // LoadFromDataAsync вычисляет: localPos = (worldPos - entityPos) / scale
            // Unity автоматически поворачивает localPos при преобразовании в мировые координаты
            // Поэтому используем обратный поворот для компенсации
            Vector3 inverseRotatedLocalPos = Quaternion.Inverse(finalRotationQuat) * rotatedLocalPos;
            Vector3 newWorldPos = newEntityPosition + inverseRotatedLocalPos * newEntityScale;

            // Применяем поворот по оси Y к повороту куба (сохраняем повороты по X и Z)
            Quaternion originalCubeRot = original.Rotation;
            float cubeYRotation = originalCubeRot.eulerAngles.y;
            float newCubeYRotation = cubeYRotation + yRotationDelta;
            Quaternion adjustedCubeRotation = Quaternion.Euler(
                originalCubeRot.eulerAngles.x,
                newCubeYRotation,
                originalCubeRot.eulerAngles.z
            );

            adjustedCubeData[i] = new CubeData(
                newWorldPos, // Новая мировая позиция
                original.Color,
                original.blockTypeId,
                original.entityId,
                adjustedCubeRotation
            );
        }

        // Entity.LoadFromDataAsync ожидает мировые позиции и конвертирует их в локальные
        yield return entity.LoadFromDataAsync(adjustedCubeData, spawner, deferredSetup: true);
        entity.FinalizeLoad();
    }

    /// <summary>
    /// Возвращает количество сохраненных сущностей
    /// </summary>
    public int GetSavedEntityCount()
    {
        return _savedEntities.Count;
    }

    /// <summary>
    /// Очищает все сохраненные сущности
    /// </summary>
    public void ClearSavedEntities()
    {
        _savedEntities.Clear();
        _currentSelectedSaveIndex = 0;
        Debug.Log("Saved entities cleared!");
        SaveAllToDisk();
    }

    /// <summary>
    /// Удаляет указанную сущность из списка сохраненных
    /// </summary>
    public void RemoveSavedEntity(int index)
    {
        if (index >= 0 && index < _savedEntities.Count)
        {
            _savedEntities.RemoveAt(index);
            Debug.Log($"Entity removed at index {index}");
            SaveAllToDisk();
        }
    }

    /// <summary>
    /// Предоставляет доступ только для чтения к данным сохраненной сущности для внешних систем (например, ghost preview)
    /// </summary>
    public bool TryGetSavedEntityData(int index, out EntitySaveData data)
    {
        data = null;
        if (_savedEntities == null || index < 0 || index >= _savedEntities.Count)
        {
            return false;
        }

        data = _savedEntities[index];
        return data != null;
    }

    private string TakeEntityScreenshot(Entity entity)
    {
        if (_screenshotCamera == null)
        {
            Debug.LogWarning("Screenshot camera is not assigned for EntitySaveManager.");
            return string.Empty;
        }

        bool prevActive = _screenshotCamera.gameObject.activeSelf;
        RenderTexture rt = null;
        int prevCullingMask = _screenshotCamera.cullingMask;
        CameraClearFlags prevClearFlags = _screenshotCamera.clearFlags;
        Color prevBackground = _screenshotCamera.backgroundColor;
        List<(Transform t, int layer)> originalLayers = new List<(Transform, int)>();
        try
        {
            // Вычисляем границы
            Renderer[] renderers = entity.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
                return string.Empty;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            Vector3 target = bounds.center;

            Transform camT = _screenshotCamera.transform;
            Vector3 baseDir = _cameraOffset.sqrMagnitude > 0.0001f
                ? _cameraOffset.normalized
                : new Vector3(0f, 0f, -1f);
            baseDir = _useObjectSpaceOffset ? (entity.transform.rotation * baseDir) : baseDir;

            // Горизонтальная составляющая и подъем на 15 градусов
            Vector3 horizontal = Vector3.ProjectOnPlane(baseDir, Vector3.up).normalized;
            if (horizontal.sqrMagnitude < 1e-4f)
            {
                horizontal = Vector3.forward; // запасной вариант
            }

            Vector3 tiltAxis = Vector3.Cross(horizontal, Vector3.up).normalized;
            Vector3 viewDirFromTarget = Quaternion.AngleAxis(15f, tiltAxis) * horizontal; // немного выше объекта

            // Расстояние для размещения всего объекта в кадре (приближение через ограничивающую сферу)
            float radius = bounds.extents.magnitude;
            float tanHalfFov = Mathf.Tan(0.5f * _screenshotCamera.fieldOfView * Mathf.Deg2Rad);
            float aspect = (float)_screenshotWidth / Mathf.Max(1, _screenshotHeight);
            float dVert = radius / Mathf.Max(1e-4f, tanHalfFov);
            float dHorz = radius / Mathf.Max(1e-4f, tanHalfFov * aspect);
            float distance = Mathf.Max(dVert, dHorz) * Mathf.Max(1.0f, _framingPadding);

            Vector3 desiredPos = target + viewDirFromTarget * distance;

            // Включаем и ориентируем камеру для снимка
            _screenshotCamera.gameObject.SetActive(true);
            camT.position = desiredPos;
            camT.rotation = Quaternion.LookRotation((target - desiredPos).normalized, Vector3.up);

            // Изолируем рендеринг: только сущность на выделенном слое
            CacheAndApplyLayerRecursive(entity.transform, _screenshotSubjectLayer, originalLayers);
            _screenshotCamera.cullingMask = 1 << _screenshotSubjectLayer;
            _screenshotCamera.clearFlags = CameraClearFlags.SolidColor;
            _screenshotCamera.backgroundColor = _screenshotBackgroundColor;

            // Настраиваем рендер с поддержкой альфа-канала
            rt = new RenderTexture(_screenshotWidth, _screenshotHeight, 24, RenderTextureFormat.ARGB32);
            _screenshotCamera.targetTexture = rt;
            Texture2D tex = new Texture2D(_screenshotWidth, _screenshotHeight, TextureFormat.RGBA32, false);
            _screenshotCamera.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, _screenshotWidth, _screenshotHeight), 0, 0);
            tex.Apply();

            byte[] png = tex.EncodeToPNG();
            string fileName = $"entity_{entity.EntityId}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string path = Path.Combine(SavesDirectoryPath, fileName);
            File.WriteAllBytes(path, png);

            return path;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to take entity screenshot: {e.Message}");
            return string.Empty;
        }
        finally
        {
            // Очистка и восстановление состояния
            if (_screenshotCamera != null)
            {
                _screenshotCamera.targetTexture = null;
                _screenshotCamera.cullingMask = prevCullingMask;
                _screenshotCamera.clearFlags = prevClearFlags;
                _screenshotCamera.backgroundColor = prevBackground;
                _screenshotCamera.gameObject.SetActive(prevActive);
            }

            if (RenderTexture.active == rt)
            {
                RenderTexture.active = null;
            }

            if (rt != null)
            {
                rt.Release();
            }

            // Восстанавливаем исходные слои иерархии сущности
            RestoreLayers(originalLayers);
        }
    }

    private void SaveAllToDisk()
    {
        try
        {
            if (!Directory.Exists(SavesDirectoryPath))
            {
                Directory.CreateDirectory(SavesDirectoryPath);
            }

            SaveFile saveFile = new SaveFile { entities = _savedEntities };
            string json = JsonUtility.ToJson(saveFile, false);
            File.WriteAllText(SavesFilePath, json);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save entities to disk: {e.Message}");
        }
    }

    private void LoadAllFromDisk()
    {
        try
        {
            if (!File.Exists(SavesFilePath))
            {
                return;
            }

            string json = File.ReadAllText(SavesFilePath);
            if (string.IsNullOrEmpty(json)) return;

            SaveFile loaded = JsonUtility.FromJson<SaveFile>(json);
            if (loaded != null && loaded.entities != null)
            {
                _savedEntities = loaded.entities;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load entities from disk: {e.Message}");
        }
    }

    private void CacheAndApplyLayerRecursive(Transform root, int newLayer, List<(Transform t, int layer)> cache)
    {
        if (root == null) return;
        cache.Add((root, root.gameObject.layer));
        root.gameObject.layer = newLayer;
        for (int i = 0; i < root.childCount; i++)
        {
            CacheAndApplyLayerRecursive(root.GetChild(i), newLayer, cache);
        }
    }

    private void RestoreLayers(List<(Transform t, int layer)> cache)
    {
        if (cache == null) return;
        for (int i = 0; i < cache.Count; i++)
        {
            var entry = cache[i];
            if (entry.t != null)
            {
                entry.t.gameObject.layer = entry.layer;
            }
        }
    }

    private void TryCreateSavedObjectUIItem(string screenshotPath, int index)
    {
        if (_savedObjectItemPrefab == null || _savedObjectsContainer == null)
        {
            return;
        }

        GameObject item = Instantiate(_savedObjectItemPrefab, _savedObjectsContainer);

        // Находим кнопку и устанавливаем её изображение
        Button button = item.GetComponentInChildren<Button>(true);
        if (button != null)
        {
            Image image = button.GetComponent<Image>();
            if (image == null)
            {
                image = button.GetComponentInChildren<Image>(true);
            }

            if (image != null && File.Exists(screenshotPath))
            {
                byte[] bytes = File.ReadAllBytes(screenshotPath);
                Texture2D texture = new Texture2D(2, 2);
                texture.LoadImage(bytes);
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));
                image.sprite = sprite;
            }

            // Привязываем кнопку для выбора этой сохраненной сущности через EntityCreator
            int capturedIndex = index;
            button.onClick.AddListener(() => InvokeEntityCreatorSelection(capturedIndex));
        }
    }

    private void CacheEntityCreator()
    {
        if (_entityCreatorBehaviour == null)
        {
            // Пытаемся найти компонент с именем 'EntityCreator' в сцене
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var b = behaviours[i];
                if (b != null && b.GetType().Name == "EntityCreator")
                {
                    _entityCreatorBehaviour = b;
                    break;
                }
            }
        }
    }

    private void InvokeEntityCreator(int index)
    {
        CacheEntityCreator();

        if (_entityCreatorBehaviour != null)
        {
            _entityCreatorBehaviour.SendMessage("SpawnSavedIndex", index, SendMessageOptions.DontRequireReceiver);
        }
        else
        {
            Debug.LogWarning("EntityCreator not found in scene to handle spawn click.");
        }
    }

    private void InvokeEntityCreatorSelection(int index)
    {
        // Обновляем текущий выбранный индекс при клике на кнопку UI
        _currentSelectedSaveIndex = index;

        CacheEntityCreator();

        if (_entityCreatorBehaviour != null)
        {
            _entityCreatorBehaviour.SendMessage("SelectSavedIndex", index, SendMessageOptions.DontRequireReceiver);
        }
        else
        {
            Debug.LogWarning("EntityCreator not found in scene to handle selection click.");
        }
    }
}
