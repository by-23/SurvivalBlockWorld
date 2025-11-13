using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Assets._Project.Scripts.UI;

public class EntityManager : MonoBehaviour
{
    [Header("Local Save Settings")] [SerializeField]
    private string _fileName = "entity.dat";

    [Header("References")] [SerializeField]
    private SaveConfig _config; // опционально: используем только для флага отложенной инициализации

    [SerializeField] private Camera _playerCamera;
    [SerializeField] private EntitySelector _selector;

    [Header("UI")] [SerializeField] private Button _savePlaceButton; // объединённая кнопка сохранения/размещения
    [SerializeField] private TMPro.TMP_Text _savePlaceButtonText; // текст кнопки
    [SerializeField] private string _saveButtonText = "Сохранить"; // текст для режима сохранения
    [SerializeField] private string _placeButtonText = "Разместить"; // текст для режима размещения
    [SerializeField] private Button _saveItemButtonPrefab; // префаб кнопки сохранённого объекта
    [SerializeField] private Transform _saveListContainer; // контейнер для кнопок
    [SerializeField] private Button _cancelGhostButton;

    [SerializeField] private CubeSpawner _cubeSpawner;
    [SerializeField] private Assets._Project.Scripts.UI.GhostEntityPlacer _ghostPlacer;
    [SerializeField] private ScreenshotManager _screenshotManager;
    private Entity _currentGhostEntity;

    [Serializable]
    private struct SingleEntitySave
    {
        public string name;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public CubeData[] cubes;
        public string screenshotId;
    }

    [Serializable]
    public class SavedEntry
    {
        public string path;
        public string name;
        public string screenshotId;
    }


    private void EnsureSpawner()
    {
        if (_cubeSpawner == null)
        {
            _cubeSpawner = FindFirstObjectByType<CubeSpawner>();
        }
    }

    private string GetSavePath()
    {
        string nameToUse = string.IsNullOrEmpty(_fileName) ? "entity.dat" : _fileName;
        return Path.Combine(Application.persistentDataPath, nameToUse);
    }

    private void OnEnable()
    {
        // Привязка UI-кнопок, если заданы в инспекторе
        if (_savePlaceButton != null)
        {
            _savePlaceButton.onClick.RemoveAllListeners();
            _savePlaceButton.onClick.AddListener(OnSavePlaceButtonPressed);

            // Получаем компонент текста, если не назначен
            if (_savePlaceButtonText == null)
            {
                _savePlaceButtonText = _savePlaceButton.GetComponentInChildren<TMPro.TMP_Text>();
            }
        }

        if (_cancelGhostButton != null)
        {
            _cancelGhostButton.onClick.RemoveAllListeners();
            _cancelGhostButton.onClick.AddListener(CancelGhost);
        }

        if (_ghostPlacer == null)
        {
            _ghostPlacer = FindFirstObjectByType<Assets._Project.Scripts.UI.GhostEntityPlacer>();
        }

        RefreshSavedList();
        UpdateGhostButtonsState();
    }

    private void OnDisable()
    {
        if (_savePlaceButton != null)
        {
            _savePlaceButton.onClick.RemoveListener(OnSavePlaceButtonPressed);
        }
    }

    private void OnSavePlaceButtonPressed()
    {
        if (IsGhostActive())
        {
            // Если ghost активен - размещаем entity
            ConfirmGhost();
        }
        else
        {
            // Если ghost не активен - сохраняем entity
            SaveLookedEntity();
        }
    }

    public void RefreshSavedList()
    {
        if (_saveListContainer == null || _saveItemButtonPrefab == null)
        {
            return;
        }

        for (int i = _saveListContainer.childCount - 1; i >= 0; i--)
        {
            var child = _saveListContainer.GetChild(i);
            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }

        var entries = GetSavedEntries();
        if (entries != null)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                CreateSaveButtonUI(entries[i].screenshotId, entries[i].path, entries[i].name);
            }
        }
    }

    public System.Collections.Generic.List<SavedEntry> GetSavedEntries()
    {
        var list = new System.Collections.Generic.List<SavedEntry>();
        try
        {
            string dir = Application.persistentDataPath;
            if (!Directory.Exists(dir)) return list;
            var files = Directory.GetFiles(dir, "entity_*.dat", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                if (TryReadMetadata(files[i], out var entry))
                {
                    list.Add(entry);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"GetSavedEntries: ошибка перечисления — {e.Message}");
        }

        return list;
    }

    private bool TryReadMetadata(string path, out SavedEntry entry)
    {
        entry = null;
        try
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(fs))
            {
                string entryName = reader.ReadString();
                // position (3), rotation (4), scale (3)
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();

                int count = reader.ReadInt32();
                long toSkip = (long)count * 31L; // 31 байт на CubeData
                if (fs.Position + toSkip <= fs.Length)
                {
                    fs.Position += toSkip;
                }

                string screenshotId = string.Empty;
                if (fs.Position < fs.Length)
                {
                    try
                    {
                        screenshotId = reader.ReadString();
                    }
                    catch
                    {
                        screenshotId = string.Empty;
                    }
                }

                entry = new SavedEntry { path = path, name = entryName, screenshotId = screenshotId };
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private string GetUniqueSavePath()
    {
        string file = $"entity_{DateTime.Now:yyyyMMdd_HHmmssfff}.dat";
        return Path.Combine(Application.persistentDataPath, file);
    }


    public Entity GetTargetEntity()
    {
        // Сначала берём из EntitySelector, если он есть
        if (_selector != null)
        {
            var hovered = _selector.GetHoveredEntity();
            if (hovered != null) return hovered;
        }

        // Фоллбэк: рейкаст из центра экрана от камеры
        var cam = _playerCamera != null ? _playerCamera : Camera.main;
        if (cam == null) return null;

        Vector3 screenCenter = new Vector3(Screen.width / 2f, Screen.height / 2f);
        Ray ray = cam.ScreenPointToRay(screenCenter);
        if (Physics.Raycast(ray, out var hit, 200f))
        {
            return hit.collider.GetComponentInParent<Entity>();
        }

        return null;
    }

    public async void SaveLookedEntity()
    {
        try
        {
            Entity target = GetTargetEntity();
            if (target == null)
            {
                Debug.LogWarning("SaveLookedEntity: цель не найдена");
                return;
            }

            // Получаем данные кубов
            target.EnsureCacheValid();
            CubeData[] cubes = target.GetSaveData();
            if (cubes == null || cubes.Length == 0)
            {
                Debug.LogWarning("SaveLookedEntity: у цели нет кубов для сохранения");
                return;
            }

            // Делаем скриншот
            if (_screenshotManager == null)
            {
                _screenshotManager = FindAnyObjectByType<ScreenshotManager>();
            }

            string screenshotId = string.Empty;
            if (_screenshotManager != null)
            {
                // Дожидаемся завершения сохранения файла и индекса — предотвращает гонки
                screenshotId = await _screenshotManager.CaptureAsync(target, null, 512, 512, _playerCamera);
            }

            // Пивот при сохранении: центр по XZ и самый низ по Y
            Vector3 savedPivot = target.transform.position;
            if (target.TryGetLocalBounds(out Bounds localBounds))
            {
                // Берём точку (center.x, min.y, center.z) в локальных координатах и переводим в мир
                Vector3 localBottomCenter = new Vector3(localBounds.center.x, localBounds.min.y, localBounds.center.z);
                savedPivot = target.transform.TransformPoint(localBottomCenter);
            }

            SingleEntitySave data = new SingleEntitySave
            {
                name = target.gameObject.name,
                position = savedPivot,
                rotation = target.transform.rotation,
                scale = target.transform.localScale,
                cubes = cubes,
                screenshotId = screenshotId
            };

            // Пишем в уникальный файл асинхронно в фоне
            string path = GetUniqueSavePath();
            string directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            byte[] buffer = await Task.Run(() => BuildSaveBytes(data));
            await File.WriteAllBytesAsync(path, buffer);

            // продолжаем на главном потоке Unity (контекст сохранён)
            await Task.Yield();
            Debug.Log($"Entity сохранён: {path}");
            CreateSaveButtonUI(data.screenshotId, path, data.name);
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveLookedEntity: ошибка сохранения — {e.Message}");
        }
    }

    private static byte[] BuildSaveBytes(SingleEntitySave data)
    {
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            writer.Write(data.name ?? string.Empty);
            writer.Write(data.position.x);
            writer.Write(data.position.y);
            writer.Write(data.position.z);

            writer.Write(data.rotation.x);
            writer.Write(data.rotation.y);
            writer.Write(data.rotation.z);
            writer.Write(data.rotation.w);

            writer.Write(data.scale.x);
            writer.Write(data.scale.y);
            writer.Write(data.scale.z);

            int count = data.cubes != null ? data.cubes.Length : 0;
            writer.Write(count);
            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    data.cubes[i].WriteTo(writer);
                }
            }

            writer.Write(data.screenshotId ?? string.Empty);
            writer.Flush();
            return ms.ToArray();
        }
    }

    private async void CreateSaveButtonUI(string screenshotId, string saveFilePath, string title)
    {
        if (_saveItemButtonPrefab == null || _saveListContainer == null)
        {
            Debug.LogWarning("CreateSaveButtonUI: не задан префаб или контейнер");
            return;
        }

        var btn = Instantiate(_saveItemButtonPrefab, _saveListContainer);
        btn.onClick.RemoveAllListeners();
        // Сохраняем путь в имени объекта для возможности поиска кнопки при удалении
        btn.gameObject.name = $"SavedEntity_{Path.GetFileName(saveFilePath)}";
        btn.onClick.AddListener(() => LoadSavedEntityFromPath(saveFilePath));

        // Назначаем спрайт скриншота на Image, расположенный на том же объекте, где и Button
        Image image = null;
        // Сначала пробуем targetGraphic, если он Image
        if (btn.targetGraphic != null && btn.targetGraphic is Image targetImg)
        {
            image = targetImg;
        }

        // Иначе берём Image на том же объекте
        if (image == null)
        {
            image = btn.GetComponent<Image>();
        }

        if (_screenshotManager == null)
        {
            _screenshotManager = FindAnyObjectByType<ScreenshotManager>();
        }

        if (image != null && _screenshotManager != null && !string.IsNullOrEmpty(screenshotId))
        {
            image.preserveAspect = true;
            await _screenshotManager.LoadToImageByIdAsync(screenshotId, image);
        }

        // Текст на кнопке (если есть)
        var text = btn.GetComponentInChildren<TMPro.TMP_Text>();
        if (text != null)
        {
            text.text = string.IsNullOrEmpty(title) ? "Entity" : title;
        }

        // Кнопка удаления, если есть дочерняя Button с именем, содержащим "Delete"
        var childButtons = btn.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < childButtons.Length; i++)
        {
            var cb = childButtons[i];
            if (cb == btn) continue;
            if (cb.name.IndexOf("Delete", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                cb.onClick.RemoveAllListeners();
                cb.onClick.AddListener(() =>
                {
                    DeleteSavedEntity(saveFilePath, screenshotId);
                    Destroy(btn.gameObject);
                });
                break;
            }
        }
    }

    public async void LoadSavedEntity()
    {
        try
        {
            string path = GetSavePath();
            if (!File.Exists(path))
            {
                Debug.LogWarning($"LoadSavedEntity: файл не найден: {path}");
                return;
            }

            SingleEntitySave data;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(fs))
            {
                data = new SingleEntitySave();
                data.name = reader.ReadString();

                Vector3 pos;
                pos.x = reader.ReadSingle();
                pos.y = reader.ReadSingle();
                pos.z = reader.ReadSingle();
                data.position = pos;

                Quaternion rot;
                rot.x = reader.ReadSingle();
                rot.y = reader.ReadSingle();
                rot.z = reader.ReadSingle();
                rot.w = reader.ReadSingle();
                data.rotation = rot;

                Vector3 scl;
                scl.x = reader.ReadSingle();
                scl.y = reader.ReadSingle();
                scl.z = reader.ReadSingle();
                data.scale = scl;

                int count = reader.ReadInt32();
                if (count > 0)
                {
                    data.cubes = new CubeData[count];
                    for (int i = 0; i < count; i++)
                    {
                        data.cubes[i] = CubeData.ReadFrom(reader);
                    }
                }
                else
                {
                    data.cubes = Array.Empty<CubeData>();
                }
            }

            if (data.cubes == null || data.cubes.Length == 0)
            {
                Debug.LogWarning("LoadSavedEntity: сохранённый набор пуст");
                return;
            }

            EnsureSpawner();
            if (_cubeSpawner == null)
            {
                Debug.LogError("LoadSavedEntity: CubeSpawner не найден в сцене");
                return;
            }

            // Создаём пустой Entity и наполняем его кубами
            Entity entity = EntityFactory.CreateEntity(
                data.position,
                data.rotation,
                data.scale,
                isKinematic: true,
                entityName: string.IsNullOrEmpty(data.name) ? "Entity" : data.name
            );

            bool deferred = _config != null ? _config.useDeferredSetup : true;
            await entity.LoadFromDataAsync(data.cubes, _cubeSpawner, deferredSetup: deferred,
                savedEntityPosition: data.position);
            if (deferred)
            {
                entity.FinalizeLoad();
            }


            Debug.Log("Entity загружен из локального файла");
        }
        catch (Exception e)
        {
            Debug.LogError($"LoadSavedEntity: ошибка загрузки — {e.Message}");
        }
    }

    public async void LoadSavedEntityFromPath(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogWarning($"LoadSavedEntityFromPath: файл не найден: {path}");
                return;
            }

            SingleEntitySave data;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(fs))
            {
                data = new SingleEntitySave();
                data.name = reader.ReadString();

                Vector3 pos;
                pos.x = reader.ReadSingle();
                pos.y = reader.ReadSingle();
                pos.z = reader.ReadSingle();
                data.position = pos;

                Quaternion rot;
                rot.x = reader.ReadSingle();
                rot.y = reader.ReadSingle();
                rot.z = reader.ReadSingle();
                rot.w = reader.ReadSingle();
                data.rotation = rot;

                Vector3 scl;
                scl.x = reader.ReadSingle();
                scl.y = reader.ReadSingle();
                scl.z = reader.ReadSingle();
                data.scale = scl;

                int count = reader.ReadInt32();
                if (count > 0)
                {
                    data.cubes = new CubeData[count];
                    for (int i = 0; i < count; i++)
                    {
                        data.cubes[i] = CubeData.ReadFrom(reader);
                    }
                }
                else
                {
                    data.cubes = Array.Empty<CubeData>();
                }

                // поле скриншота может отсутствовать в старых файлах — защищаемся
                if (fs.Position < fs.Length)
                {
                    try
                    {
                        data.screenshotId = reader.ReadString();
                    }
                    catch
                    {
                        /* совместимость */
                    }
                }
            }

            if (data.cubes == null || data.cubes.Length == 0)
            {
                Debug.LogWarning("LoadSavedEntityFromPath: сохранённый набор пуст");
                return;
            }

            EnsureSpawner();
            if (_cubeSpawner == null)
            {
                Debug.LogError("LoadSavedEntityFromPath: CubeSpawner не найден в сцене");
                return;
            }

            Entity entity = EntityFactory.CreateEntity(
                Vector3.zero, // позиция будет установлена ghost placer'ом
                Quaternion.identity,
                data.scale,
                isKinematic: true,
                entityName: string.IsNullOrEmpty(data.name) ? "Entity" : data.name
            );

            bool deferred = _config != null ? _config.useDeferredSetup : true;
            await entity.LoadFromDataAsync(data.cubes, _cubeSpawner, deferredSetup: deferred,
                savedEntityPosition: data.position);
            if (deferred)
            {
                entity.FinalizeLoad();
            }


            // Отменяем предыдущий ghost, если он существует
            if (_ghostPlacer != null && _ghostPlacer.IsActive)
            {
                _ghostPlacer.Cancel();
                _currentGhostEntity = null;
                UpdateGhostButtonsState();
            }

            // Переходим в ghost-режим вместо финального размещения
            if (_ghostPlacer != null)
            {
                _currentGhostEntity = entity;
                _ghostPlacer.Begin(entity, _playerCamera);
                UpdateGhostButtonsState();
                Debug.Log("Entity загружен в ghost-режиме");
            }
            else
            {
                Debug.LogWarning("GhostPlacer не найден, размещаем entity напрямую");
                entity.transform.position = data.position;
                entity.transform.rotation = data.rotation;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"LoadSavedEntityFromPath: ошибка загрузки — {e.Message}");
        }
    }

    public void DeleteSavedEntity(string path, string screenshotId)
    {
        // Удаляем кнопку из UI, если контейнер задан
        if (_saveListContainer != null)
        {
            string fileName = Path.GetFileName(path);
            string targetName = $"SavedEntity_{fileName}";
            for (int i = _saveListContainer.childCount - 1; i >= 0; i--)
            {
                var child = _saveListContainer.GetChild(i);
                if (child.name == targetName)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(child.gameObject);
                    }
                    else
                    {
                        DestroyImmediate(child.gameObject);
                    }

                    break;
                }
            }
        }

        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"DeleteSavedEntity: не удалось удалить файл {path}: {e.Message}");
        }

        try
        {
            if (!string.IsNullOrEmpty(screenshotId))
            {
                if (_screenshotManager == null)
                    _screenshotManager = FindAnyObjectByType<ScreenshotManager>();

                if (_screenshotManager != null)
                {
                    // Режим игры: используем менеджер
                    _screenshotManager.DeleteScreenshot(screenshotId);
                    _ = _screenshotManager.SaveIndexAsync();
                }
                else
                {
                    // Режим редактора (менеджера нет): удаляем вручную из индекса и с диска
                    string indexPath = Path.Combine(Application.persistentDataPath, "screenshots.json");
                    string json = File.Exists(indexPath) ? File.ReadAllText(indexPath) : string.Empty;
                    if (!string.IsNullOrEmpty(json))
                    {
                        try
                        {
                            ScreenshotIndexDTO dto = JsonUtility.FromJson<ScreenshotIndexDTO>(json);
                            if (dto != null && dto.Entries != null)
                            {
                                // Найдём путь и удалим запись
                                string ssPath = null;
                                for (int i = dto.Entries.Count - 1; i >= 0; i--)
                                {
                                    if (dto.Entries[i] != null && dto.Entries[i].Id == screenshotId)
                                    {
                                        ssPath = dto.Entries[i].Path;
                                        dto.Entries.RemoveAt(i);
                                        break;
                                    }
                                }

                                if (!string.IsNullOrEmpty(ssPath) && File.Exists(ssPath))
                                {
                                    try
                                    {
                                        File.Delete(ssPath);
                                    }
                                    catch
                                    {
                                        /* ignore */
                                    }
                                }

                                // Записываем обновлённый индекс
                                string outJson = JsonUtility.ToJson(dto, false);
                                File.WriteAllText(indexPath, outJson);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"DeleteSavedEntity(Editor): ошибка обновления индекса: {ex.Message}");
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"DeleteSavedEntity: не удалось удалить скриншот {screenshotId}: {e.Message}");
        }
    }

    // DTO для работы с индексом скриншотов в редакторе (копия структуры из ScreenshotManager)
    [Serializable]
    private class ScreenshotIndexDTO
    {
        public System.Collections.Generic.List<ScreenshotEntryDTO> Entries;
    }

    [Serializable]
    private class ScreenshotEntryDTO
    {
        public string Id;
        public string Path;
    }

    public void ConfirmGhost()
    {
        if (_ghostPlacer != null && _ghostPlacer.TryConfirm())
        {
            _currentGhostEntity = null;
            UpdateGhostButtonsState();
            Debug.Log("Ghost entity подтверждён и размещён");
        }
        else
        {
            Debug.LogWarning("Нельзя подтвердить ghost: объект заблокирован или не активен");
        }
    }

    public void CancelGhost()
    {
        if (_ghostPlacer != null)
        {
            _ghostPlacer.Cancel();
            _currentGhostEntity = null;
            UpdateGhostButtonsState();
        }
    }

    /// <summary>
    /// Проверяет, активен ли ghost entity в данный момент
    /// </summary>
    public bool IsGhostActive()
    {
        return _ghostPlacer != null && _ghostPlacer.IsActive;
    }

    /// <summary>
    /// Обновляет состояние кнопок в зависимости от активности ghost
    /// </summary>
    private void UpdateGhostButtonsState()
    {
        bool isActive = IsGhostActive();

        if (_cancelGhostButton != null)
        {
            _cancelGhostButton.gameObject.SetActive(isActive);
        }

        // Обновляем объединённую кнопку сохранения/размещения
        if (_savePlaceButton != null)
        {
            _savePlaceButton.interactable = true; // кнопка всегда активна

            // Обновляем текст в зависимости от режима
            if (_savePlaceButtonText != null)
            {
                _savePlaceButtonText.text = isActive ? _placeButtonText : _saveButtonText;
            }
        }
    }
}


