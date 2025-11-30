using Assets._Project.Scripts.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using static UnityEngine.GraphicsBuffer;

public class EntityManager : MonoBehaviour
{
    private static EntityManager _instance;


    [Header("Local Save Settings")] [SerializeField]
    private string _fileName = "entity.dat";

    [Header("References")] [SerializeField]
    private SaveConfig _config;

    [SerializeField] private Camera _playerCamera;
    [SerializeField] private EntitySelector _selector;
    [SerializeField] private EntityMover _mover;

    [Header("UI")] [SerializeField] private Button _savePlaceButton;
    [SerializeField] private Image _savePlaceIcon;
    [SerializeField] private Sprite _SaveIcon, _PlaceIcon;
    [SerializeField] private Button _saveItemButtonPrefab;
    [SerializeField] private Transform _saveListContainer;
    [SerializeField] private Button _cancelGhostButton;

    [SerializeField] private CubeSpawner _cubeSpawner;
    [SerializeField] private Assets._Project.Scripts.UI.GhostEntityPlacer _ghostPlacer;
    [SerializeField] private ScreenshotManager _screenshotManager;
    private Entity _currentGhostEntity;
    private SaveSystem _saveSystem;
    private Dictionary<string, string> _localPathToFirebaseId = new Dictionary<string, string>();

    public bool IsEntityInFirebase(string path)
    {
        return _localPathToFirebaseId.ContainsKey(path);
    }

    [Header("Entity Settings")] [SerializeField]
    private float _maxWaitTimeForStop = 120f;

    [SerializeField] private float _destroyDuration = 1f;

    public static float MaxWaitTimeForStop => _instance != null ? _instance._maxWaitTimeForStop : 120f;
    public static float DestroyDuration => _instance != null ? _instance._destroyDuration : 1f;

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

    private void Awake()
    {
        _instance = this;
        SaveTakeButtonActive(false);
        _saveSystem = FindFirstObjectByType<SaveSystem>();
    }

    private void OnEnable()
    {
        if (_savePlaceButton != null)
        {
            _savePlaceButton.onClick.RemoveAllListeners();
            _savePlaceButton.onClick.AddListener(OnSavePlaceButtonPressed);

            if (_savePlaceIcon != null)
            {
                _savePlaceIcon.sprite = _SaveIcon;
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

        UpdateGhostButtonsState();
        LoadSharedEntitiesFromFirebase();
    }

    private void OnDisable()
    {
        if (_savePlaceButton != null)
        {
            _savePlaceButton.onClick.RemoveListener(OnSavePlaceButtonPressed);
        }
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    private void OnSavePlaceButtonPressed()
    {
        if (IsGhostActive())
        {
            ConfirmGhost();
        }
        else
        {
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
                long toSkip = (long)count * 31L;
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
        if (_selector != null)
        {
            var hovered = _selector.GetHoveredEntity();
            if (hovered != null) return hovered;
        }

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

            target.EnsureCacheValid();
            CubeData[] cubes = target.GetSaveData();
            if (cubes == null || cubes.Length == 0)
            {
                Debug.LogWarning("SaveLookedEntity: у цели нет кубов для сохранения");
                return;
            }

            if (_screenshotManager == null)
            {
                _screenshotManager = FindAnyObjectByType<ScreenshotManager>();
            }

            string screenshotId = string.Empty;
            if (_screenshotManager != null)
            {
                screenshotId = await _screenshotManager.CaptureAsync(target, null, 512, 512, _playerCamera);
            }

            Vector3 savedPivot = target.transform.position;
            if (target.TryGetLocalBounds(out Bounds localBounds))
            {
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

            string path = GetUniqueSavePath();
            string directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            byte[] buffer = await Task.Run(() => BuildSaveBytes(data));
            await File.WriteAllBytesAsync(path, buffer);

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
        btn.gameObject.name = $"SavedEntity_{Path.GetFileName(saveFilePath)}";
        btn.onClick.AddListener(() => LoadSavedEntityFromPath(saveFilePath));

        Image image = null;

        if (image == null)
            image = btn.GetComponent<SaveSlotObj>()._iconImg;

        if (_screenshotManager == null)
        {
            _screenshotManager = FindAnyObjectByType<ScreenshotManager>();
        }

        if (image != null && !string.IsNullOrEmpty(screenshotId))
        {
            image.preserveAspect = true;

            // Если в ячейке хранится URL — грузим картинку напрямую из сети
            if (IsUrl(screenshotId))
            {
                await LoadImageFromUrlAsync(screenshotId, image);
            }
            // Иначе считаем, что это локальный id скриншота и используем ScreenshotManager
            else
            {
                if (_screenshotManager == null)
                {
                    _screenshotManager = FindAnyObjectByType<ScreenshotManager>();
                }

                if (_screenshotManager != null)
                {
                    await _screenshotManager.LoadToImageByIdAsync(screenshotId, image);
                }
            }
        }

        var text = btn.GetComponentInChildren<TMPro.TMP_Text>();
        if (text != null)
        {
            text.text = string.IsNullOrEmpty(title) ? "Entity" : title;
        }

        var childButtons = btn.GetComponentsInChildren<Button>(true);

        for (int i = 0; i < childButtons.Length; i++)
        {
            var cb = childButtons[i];
            if (cb == btn) continue;

            string buttonName = cb.name.ToLower();
            if (buttonName.IndexOf("delete", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                cb.onClick.RemoveAllListeners();
                cb.onClick.AddListener(() =>
                {
                    DeleteSavedEntity(saveFilePath, screenshotId);
                    Destroy(btn.gameObject);
                });
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
                Vector3.zero,
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

                Vector3Int targetPos = new Vector3Int((int)data.position.x, (int)data.position.y, (int)data.position.z);
                entity.transform.position = targetPos;
                entity.transform.rotation = data.rotation;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"LoadSavedEntityFromPath: ошибка загрузки — {e.Message}");
        }
    }

    public async void DeleteFromFirebaseOnly(string path)
    {
        if (!_localPathToFirebaseId.TryGetValue(path, out string firebaseId))
        {
            Debug.LogWarning("Entity не найден в Firebase");
            return;
        }

        if (_saveSystem != null && _config != null && _config.useFirebase)
        {
            try
            {
                var firebaseAdapter = GetFirebaseAdapter();
                if (firebaseAdapter != null)
                {
                    bool success = await firebaseAdapter.DeleteSharedEntityAsync(firebaseId);
                    if (success)
                    {
                        _localPathToFirebaseId.Remove(path);
                        Debug.Log($"Entity удален из Firebase: {firebaseId}");
                    }
                    else
                    {
                        Debug.LogError("Не удалось удалить entity из Firebase");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Ошибка удаления entity из Firebase: {e.Message}");
            }
        }
    }

    public async void DeleteSavedEntity(string path, string screenshotId)
    {
        bool isSharedEntity = _localPathToFirebaseId.TryGetValue(path, out string firebaseId);

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

                if (isSharedEntity)
                {
                    _localPathToFirebaseId.Remove(path);
                }
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
                    _screenshotManager.DeleteScreenshot(screenshotId);
                    _ = _screenshotManager.SaveIndexAsync();
                }
                else
                {
                    string indexPath = Path.Combine(Application.persistentDataPath, "screenshots.json");
                    string json = File.Exists(indexPath) ? File.ReadAllText(indexPath) : string.Empty;
                    if (!string.IsNullOrEmpty(json))
                    {
                        try
                        {
                            ScreenshotIndexDTO dto = JsonUtility.FromJson<ScreenshotIndexDTO>(json);
                            if (dto != null && dto.Entries != null)
                            {
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

    public bool IsGhostActive()
    {
        return _ghostPlacer != null && _ghostPlacer.IsActive;
    }

    public bool TryGetFirebaseEntityId(string localPath, out string firebaseId)
    {
        return _localPathToFirebaseId.TryGetValue(localPath, out firebaseId);
    }

    private bool IsUrl(string value)
    {
        return !string.IsNullOrEmpty(value) &&
               (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
    }

    private async Task LoadImageFromUrlAsync(string url, Image target)
    {
        if (target == null || string.IsNullOrEmpty(url))
            return;

        using (var request = UnityWebRequestTexture.GetTexture(url))
        {
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

#if UNITY_2020_1_OR_NEWER
            if (request.result != UnityWebRequest.Result.Success)
#else
            if (request.isNetworkError || request.isHttpError)
#endif
            {
                Debug.LogWarning(
                    $"[EntityManager] Не удалось загрузить скриншот по URL: {url}. Error: {request.error}");
                return;
            }

            var texture = DownloadHandlerTexture.GetContent(request);
            if (texture == null)
            {
                Debug.LogWarning($"[EntityManager] Пустой texture из URL: {url}");
                return;
            }

            var sprite = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f));

            if (target != null)
            {
                target.color = Color.white;
                target.sprite = sprite;
                target.enabled = true;
            }
        }
    }

    public void SaveTakeButtonActive(bool active)
    {
        if (IsGhostActive()) active = true;
        if (_mover.IsHolding) active = false;

        _savePlaceButton.gameObject.SetActive(active);
    }

    private void UpdateGhostButtonsState()
    {
        bool isActive = IsGhostActive();

        if (_cancelGhostButton != null)
        {
            _cancelGhostButton.gameObject.SetActive(isActive);
        }

        if (_savePlaceButton != null)
        {
            _savePlaceButton.interactable = true;

            if (_savePlaceIcon != null)
            {
                _savePlaceIcon.sprite = isActive ? _PlaceIcon : _SaveIcon;
            }
        }
    }

    private FirebaseAdapter GetFirebaseAdapter()
    {
        if (_saveSystem == null)
        {
            _saveSystem = FindFirstObjectByType<SaveSystem>();
        }

        if (_saveSystem == null)
        {
            return null;
        }

        return _saveSystem.GetFirebaseAdapter();
    }

    public async void SaveEntityToFirebaseFromEditor(string saveFilePath, string screenshotId, string title)
    {
        Debug.Log(
            $"[EntityManager] SaveEntityToFirebaseFromEditor start. Path='{saveFilePath}', ScreenshotId='{screenshotId}', Title='{title}'");

        if (_config == null || !_config.useFirebase)
        {
            Debug.LogWarning("[EntityManager] Firebase отключен в конфигурации или SaveConfig не назначен");
            return;
        }

        if (_localPathToFirebaseId.ContainsKey(saveFilePath))
        {
            Debug.LogWarning($"Entity уже сохранен в Firebase: {_localPathToFirebaseId[saveFilePath]}");
            return;
        }

        try
        {
            if (!File.Exists(saveFilePath))
            {
                Debug.LogError($"[EntityManager] Файл не найден: {saveFilePath}");
                return;
            }

            SingleEntitySave data;
            using (var fs = new FileStream(saveFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
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

                if (fs.Position < fs.Length)
                {
                    try
                    {
                        data.screenshotId = reader.ReadString();
                    }
                    catch
                    {
                        data.screenshotId = screenshotId ?? string.Empty;
                    }
                }
                else
                {
                    data.screenshotId = screenshotId ?? string.Empty;
                }
            }

            Debug.Log(
                $"[EntityManager] Прочитан файл entity. Cubes={data.cubes?.Length ?? 0}, ScreenshotId='{data.screenshotId}'");

            if (data.cubes == null || data.cubes.Length == 0)
            {
                Debug.LogWarning("[EntityManager] Нет данных кубов для сохранения в Firebase");
                return;
            }

            var firebaseAdapter = GetFirebaseAdapter();
            if (firebaseAdapter == null)
            {
                Debug.LogError("[EntityManager] FirebaseAdapter не доступен (GetFirebaseAdapter вернул null)");
                return;
            }

            Debug.Log("[EntityManager] FirebaseAdapter получен, начинаем сохранение в Firebase...");

            string entityId = $"entity_{DateTime.Now:yyyyMMdd_HHmmssfff}_{Guid.NewGuid().ToString().Substring(0, 8)}";
            bool success = await firebaseAdapter.SaveSharedEntityAsync(
                entityId,
                data.cubes,
                data.screenshotId
            );

            Debug.Log($"[EntityManager] Результат SaveSharedEntityAsync для '{entityId}': success={success}");

            if (success)
            {
                Debug.Log($"[EntityManager] Результат SaveSharedEntityAsync для '{entityId}': success={success}");

                // Сначала удаляем локальный исходный файл и скриншот
                DeleteSavedEntity(saveFilePath, data.screenshotId);

                // Загружаем данные сущности из Firebase, чтобы получить финальный screenshotId (URL после загрузки в Storage)
                string finalScreenshotId = string.Empty;
                try
                {
                    var sharedEntityData = await firebaseAdapter.LoadSharedEntityAsync(entityId);
                    if (sharedEntityData != null)
                    {
                        finalScreenshotId = sharedEntityData.ScreenshotId ?? string.Empty;
                    }
                }
                catch (Exception loadEx)
                {
                    Debug.LogWarning(
                        $"[EntityManager] Не удалось получить данные shared entity '{entityId}' после сохранения: {loadEx.Message}");
                }

                // Затем создаём локальный "shared" файл, как при загрузке из Firebase,
                // чтобы элемент сразу остался в списке и был привязан к entityId
                string sharedFileName = $"entity_shared_{entityId}.dat";
                string sharedPath = Path.Combine(Application.persistentDataPath, sharedFileName);

                Vector3 defaultScale = _config != null ? Vector3.one * _config.entityScale : Vector3.one;
                Vector3 centerPos = CalculateCubesCenter(data.cubes);
                SingleEntitySave sharedData = new SingleEntitySave
                {
                    name = string.IsNullOrEmpty(title) ? "Shared Entity" : title,
                    position = centerPos,
                    rotation = Quaternion.identity,
                    scale = defaultScale,
                    cubes = data.cubes,
                    // Сохраняем финальный screenshotId (обычно URL), чтобы UI мог сразу подгрузить картинку
                    screenshotId = finalScreenshotId
                };

                try
                {
                    string directory = Path.GetDirectoryName(sharedPath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    byte[] buffer = await Task.Run(() => BuildSaveBytes(sharedData));
                    await File.WriteAllBytesAsync(sharedPath, buffer);

                    _localPathToFirebaseId[sharedPath] = entityId;
                }
                catch (Exception createEx)
                {
                    Debug.LogWarning(
                        $"[EntityManager] Не удалось создать shared-файл для '{entityId}': {createEx.Message}");
                }

                RefreshSavedList();
            }
            else
            {
                Debug.LogError("SaveEntityToFirebaseFromEditor: Не удалось сохранить entity в Firebase");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Ошибка сохранения entity в Firebase: {e.Message}");
        }
    }

    private async void LoadSharedEntitiesFromFirebase()
    {
        if (_config == null || !_config.useFirebase)
        {
            return;
        }

        try
        {
            var firebaseAdapter = GetFirebaseAdapter();
            if (firebaseAdapter == null)
            {
                return;
            }

            var metadataList = await firebaseAdapter.GetAllSharedEntitiesMetadataAsync();
            if (metadataList == null || metadataList.Count == 0)
            {
                RefreshSavedList();
                return;
            }

            foreach (var metadata in metadataList)
            {
                var entityData = await firebaseAdapter.LoadSharedEntityAsync(metadata.EntityId);
                if (entityData == null || entityData.Cubes == null || entityData.Cubes.Length == 0)
                {
                    continue;
                }

                string fileName = $"entity_shared_{metadata.EntityId}.dat";
                string path = Path.Combine(Application.persistentDataPath, fileName);

                bool fileExists = File.Exists(path);
                bool hasMapping = _localPathToFirebaseId.ContainsKey(path);

                if (!fileExists || (!hasMapping && fileExists))
                {
                    Vector3 defaultScale = _config != null ? Vector3.one * _config.entityScale : Vector3.one;
                    Vector3 centerPos = CalculateCubesCenter(entityData.Cubes);
                    SingleEntitySave saveData = new SingleEntitySave
                    {
                        name = "Shared Entity",
                        position = centerPos,
                        rotation = Quaternion.identity,
                        scale = defaultScale,
                        cubes = entityData.Cubes,
                        screenshotId = entityData.ScreenshotId
                    };

                    string directory = Path.GetDirectoryName(path);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    byte[] buffer = await Task.Run(() => BuildSaveBytes(saveData));
                    await File.WriteAllBytesAsync(path, buffer);
                    _localPathToFirebaseId[path] = metadata.EntityId;
                }
            }

            RefreshSavedList();
        }
        catch (Exception e)
        {
            Debug.LogError($"Ошибка загрузки общих entities из Firebase: {e.Message}");
        }
    }

    private Vector3 CalculateCubesCenter(CubeData[] cubes)
    {
        if (cubes == null || cubes.Length == 0)
            return Vector3.zero;

        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        for (int i = 0; i < cubes.Length; i++)
        {
            Vector3 pos = cubes[i].Position;
            min = Vector3.Min(min, pos);
            max = Vector3.Max(max, pos);
        }

        return (min + max) * 0.5f;
    }
}




