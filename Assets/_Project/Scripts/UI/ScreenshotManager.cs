using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using Unity.Collections;

namespace Assets._Project.Scripts.UI
{
    /// <summary>
    /// Управляет созданием скриншотов сущностей
    /// </summary>
    public class ScreenshotManager : Singleton<ScreenshotManager>
    {
        [Header("Screenshot Settings")] [SerializeField]
        private Camera _screenshotCamera;

        [SerializeField] private Vector3 _cameraOffset = new Vector3(0f, 2f, -4f);
        [SerializeField] private bool _useObjectSpaceOffset = true;
        [SerializeField] private int _screenshotWidth = 512; // размер по умолчанию
        [SerializeField] private int _screenshotHeight = 512; // размер по умолчанию
        [SerializeField] private float _framingPadding = 1.1f;

        [Header("Screenshot Isolation")] [SerializeField]
        private int _screenshotSubjectLayer = 30;

        [SerializeField] private Color _screenshotBackgroundColor = new Color(0f, 0f, 0f, 0f);

        private string SavesDirectoryPath => Application.persistentDataPath;

        // Словарь id -> абсолютный путь скриншота (в памяти устройства)
        private readonly Dictionary<string, string> _idToPath = new Dictionary<string, string>();

        // Файл индекса для восстановления соответствий между сессиями
        private string IndexFilePath => Path.Combine(SavesDirectoryPath, "screenshots.json");

        // Скрипт исполнитель: не делает ничего сам по себе. Камера назначается извне при необходимости.

        /// <summary>
        /// Установить камеру для скриншотов (опционально)
        /// </summary>
        public void SetCamera(Camera cam)
        {
            _screenshotCamera = cam;
        }

        /// <summary>
        /// Сделать скриншот и сохранить в память устройства.
        /// Возвращает true/false и out id скриншота. Можно задать размеры и/или камеру.
        /// </summary>
        public bool TryCapture(Entity entity, out string screenshotId, int? width = null, int? height = null,
            Camera cameraOverride = null)
        {
            screenshotId = string.Empty;
            string id = Guid.NewGuid().ToString("N");
            // Запускаем асинхронно, не блокируя основной поток
            _ = CaptureAsync(entity, id, width, height, cameraOverride);
            screenshotId = id;
            return true; // считаем успешным старт задачи
        }

        /// <summary>
        /// Сделать скриншот с заданным id (если нужно управлять id снаружи)
        /// </summary>
        public bool TryCaptureWithId(Entity entity, string screenshotId, int? width = null, int? height = null,
            Camera cameraOverride = null)
        {
            if (string.IsNullOrEmpty(screenshotId)) return false;
            _ = CaptureAsync(entity, screenshotId, width, height, cameraOverride);
            return true; // запуск задачи
        }

        /// <summary>
        /// Асинхронный захват: завершится, когда файл сохранён и индекс обновлён. Возвращает id.
        /// </summary>
        public Task<string> CaptureAsync(Entity entity, string screenshotId = null, int? width = null,
            int? height = null,
            Camera cameraOverride = null)
        {
            var tcs = new TaskCompletionSource<string>();
            if (entity == null)
            {
                tcs.SetResult(string.Empty);
                return tcs.Task;
            }

            string id = string.IsNullOrEmpty(screenshotId) ? Guid.NewGuid().ToString("N") : screenshotId;

            // Основной поток: готовим камеру и делаем Render
            int w = Mathf.Max(1, width ?? _screenshotWidth);
            int h = Mathf.Max(1, height ?? _screenshotHeight);

            Camera cam = cameraOverride != null ? cameraOverride : _screenshotCamera;
            if (cam == null)
            {
                Debug.LogWarning("Screenshot camera is not assigned for ScreenshotManager.");
                tcs.SetResult(string.Empty);
                return tcs.Task;
            }

            bool prevActive = cam.gameObject.activeSelf;
            int prevCullingMask = cam.cullingMask;
            CameraClearFlags prevClearFlags = cam.clearFlags;
            Color prevBackground = cam.backgroundColor;

            var originalLayers = new List<(Transform t, int layer)>();
            RenderTexture rt = null;
            try
            {
                // Изолируем сцену
                CacheAndApplyLayerRecursive(entity.transform, _screenshotSubjectLayer, originalLayers);

                // Рендер
                rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = _screenshotBackgroundColor;
                cam.cullingMask = 1 << _screenshotSubjectLayer;
                cam.gameObject.SetActive(true);

                // Расставляем камеру
                var renderers = entity.GetComponentsInChildren<Renderer>();
                if (renderers == null || renderers.Length == 0)
                {
                    tcs.SetResult(string.Empty);
                }
                else
                {
                    Bounds bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                    Vector3 target = bounds.center;
                    Vector3 baseDir = _cameraOffset.sqrMagnitude > 0.0001f
                        ? _cameraOffset.normalized
                        : new Vector3(0f, 0f, -1f);
                    baseDir = _useObjectSpaceOffset ? (entity.transform.rotation * baseDir) : baseDir;
                    Vector3 horizontal = Vector3.ProjectOnPlane(baseDir, Vector3.up).normalized;
                    if (horizontal.sqrMagnitude < 1e-4f) horizontal = Vector3.forward;
                    Vector3 tiltAxis = Vector3.Cross(horizontal, Vector3.up).normalized;
                    Vector3 viewDir = Quaternion.AngleAxis(15f, tiltAxis) * horizontal;
                    float radius = bounds.extents.magnitude;
                    float tanHalfFov = Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad);
                    float aspect = (float)w / Mathf.Max(1, h);
                    float dVert = radius / Mathf.Max(1e-4f, tanHalfFov);
                    float dHorz = radius / Mathf.Max(1e-4f, tanHalfFov * aspect);
                    float distance = Mathf.Max(dVert, dHorz) * Mathf.Max(1.0f, _framingPadding);
                    Vector3 desiredPos = target + viewDir * distance;
                    cam.transform.position = desiredPos;
                    cam.transform.rotation = Quaternion.LookRotation((target - desiredPos).normalized, Vector3.up);

                    cam.Render();

#if UNITY_2018_2_OR_NEWER
                    // Асинхронное считывание с GPU (через колбэк)
                    AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32,
                        op => { _ = ProcessReadbackAsync(op, w, h, entity, id, rt, tcs); });
#else
                     // Фоллбек: синхронное чтение (менее предпочтительно)
                     RenderTexture.active = rt;
                     var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                     tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                     tex.Apply();
                     byte[] png = tex.EncodeToPNG();
                     string fileName = $"entity_{entity.EntityId}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                     string path = Path.Combine(SavesDirectoryPath, fileName);
                     await File.WriteAllBytesAsync(path, png);
                     _idToPath[id] = path;
                     tcs.SetResult(id);
                     RenderTexture.active = null;
                     UnityEngine.Object.DestroyImmediate(tex);
                     if (rt != null) rt.Release();
#endif
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to schedule capture: {ex.Message}");
                if (rt != null) rt.Release();
                tcs.SetResult(string.Empty);
            }
            finally
            {
                // Восстанавливаем состояние камеры и слои сразу после постановки запроса
                if (cam != null)
                {
                    cam.targetTexture = null;
                    cam.cullingMask = prevCullingMask;
                    cam.clearFlags = prevClearFlags;
                    cam.backgroundColor = prevBackground;
                    cam.gameObject.SetActive(prevActive);
                }

                RestoreLayers(originalLayers);
            }

            return tcs.Task;
        }

        /// <summary>
        /// Загрузить скриншот по id в UI Image. Возвращает успешность операции.
        /// </summary>
        public async Task<bool> LoadToImageByIdAsync(string screenshotId, Image image)
        {
            if (image == null) return false;
            if (!_idToPath.TryGetValue(screenshotId, out var path) || string.IsNullOrEmpty(path)) return false;
            await LoadScreenshotAsync(image, path);
            return image.sprite != null;
        }

        /// <summary>
        /// Получить абсолютный путь к файлу скриншота по id
        /// </summary>
        public bool TryGetPath(string screenshotId, out string path)
        {
            return _idToPath.TryGetValue(screenshotId, out path);
        }

        /// <summary>
        /// Перебор всех сохранённых скриншотов (id -> path)
        /// </summary>
        public IEnumerable<KeyValuePair<string, string>> GetAllScreenshots()
        {
            return _idToPath;
        }

        /// <summary>
        /// Обновить индекс: перечитать с диска и (опционально) просканировать папку на наличие файлов без записей.
        /// </summary>
        public async Task RefreshIndexAsync(bool scanFolder = true)
        {
            await LoadIndexAsync(clearBefore: true);
            if (!scanFolder)
            {
                await SaveIndexAsync();
                return;
            }

            try
            {
                if (!Directory.Exists(SavesDirectoryPath))
                {
                    await SaveIndexAsync();
                    return;
                }

                var existing = new HashSet<string>(_idToPath.Values, StringComparer.OrdinalIgnoreCase);
                var pngs = Directory.GetFiles(SavesDirectoryPath, "*.png", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < pngs.Length; i++)
                {
                    var p = pngs[i];
                    if (string.IsNullOrEmpty(p) || existing.Contains(p)) continue;
                    // Используем имя файла как стабильный id
                    string id = Path.GetFileNameWithoutExtension(p);
                    if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N");
                    _idToPath[id] = p;
                }

                await SaveIndexAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"RefreshIndexAsync failed: {e.Message}");
            }
        }

        /// <summary>
        /// Удалить скриншот по id (удаляет файл и запись из индекса)
        /// </summary>
        public bool DeleteScreenshot(string screenshotId)
        {
            if (string.IsNullOrEmpty(screenshotId)) return false;
            if (!_idToPath.TryGetValue(screenshotId, out var path)) return false;

            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    // Несколько попыток на случай временной блокировки файла ОС
                    const int maxAttempts = 3;
                    for (int attempt = 0; attempt < maxAttempts; attempt++)
                    {
                        try
                        {
                            File.Delete(path);
                            if (!File.Exists(path)) break;
                        }
                        catch (IOException)
                        {
                            System.GC.Collect();
                            System.GC.WaitForPendingFinalizers();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to delete screenshot file: {e.Message}");
            }

            bool removed = _idToPath.Remove(screenshotId);
            _ = SaveIndexAsync(); // без ожидания
            return removed;
        }

        /// <summary>
        /// Сохранить индекс (id->path) на диск
        /// </summary>
        public async Task SaveIndexAsync()
        {
            try
            {
                if (!Directory.Exists(SavesDirectoryPath))
                {
                    Directory.CreateDirectory(SavesDirectoryPath);
                }

                var entries = new List<ScreenshotEntry>(_idToPath.Count);
                foreach (var kvp in _idToPath)
                {
                    entries.Add(new ScreenshotEntry { Id = kvp.Key, Path = kvp.Value });
                }

                var dto = new ScreenshotIndex { Entries = entries };
                string json = JsonUtility.ToJson(dto, false);
                await File.WriteAllTextAsync(IndexFilePath, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to save screenshot index: {e.Message}");
            }
        }

        /// <summary>
        /// Загрузить индекс (id->path) с диска. Можно очистить текущие записи или слить.
        /// </summary>
        public async Task LoadIndexAsync(bool clearBefore = true)
        {
            try
            {
                if (!File.Exists(IndexFilePath)) return;
                string json = await File.ReadAllTextAsync(IndexFilePath);
                if (string.IsNullOrEmpty(json)) return;
                var dto = JsonUtility.FromJson<ScreenshotIndex>(json);
                if (dto?.Entries == null) return;
                if (clearBefore) _idToPath.Clear();
                for (int i = 0; i < dto.Entries.Count; i++)
                {
                    var e = dto.Entries[i];
                    if (!string.IsNullOrEmpty(e.Id) && !string.IsNullOrEmpty(e.Path))
                    {
                        _idToPath[e.Id] = e.Path;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to load screenshot index: {e.Message}");
            }
        }

        // Формат индекса для JsonUtility
        [Serializable]
        private class ScreenshotIndex
        {
            public List<ScreenshotEntry> Entries;
        }

        [Serializable]
        private struct ScreenshotEntry
        {
            public string Id;
            public string Path;
        }

        // Внутренний исполнитель захвата скриншота с поддержкой размеров и камеры
        // Синхронный метод больше не используется; оставлен для совместимости (не вызывается)


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


        // WriteScreenshotAsync был удалён как неиспользуемый

        /// <summary>
        /// Загружает скриншот в Image компонент
        /// </summary>
        public async Task LoadScreenshotAsync(Image image, string screenshotPath)
        {
            try
            {
                bool fileExists;
                try
                {
                    fileExists = File.Exists(screenshotPath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"File.Exists check failed for screenshot: {ex.Message}");
                    fileExists = false;
                }

                if (!fileExists)
                {
                    Debug.LogWarning($"Screenshot file does not exist: {screenshotPath}");
                    return;
                }

                byte[] bytes = await File.ReadAllBytesAsync(screenshotPath);
                if (bytes == null || bytes.Length == 0)
                {
                    Debug.LogWarning($"Screenshot file is empty: {screenshotPath}");
                    return;
                }

                StartCoroutine(LoadScreenshotCoroutine(image, bytes, screenshotPath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to load screenshot: {e.Message}. Path: {screenshotPath}");
            }
        }

        private System.Collections.IEnumerator LoadScreenshotCoroutine(Image image, byte[] bytes, string screenshotPath)
        {
            if (image == null || bytes == null || bytes.Length == 0)
                yield break;

            yield return null;

            Texture2D texture = new Texture2D(2, 2);
            if (texture.LoadImage(bytes))
            {
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));
                if (image != null)
                {
                    image.sprite = sprite;
                }
            }
            else
            {
                Debug.LogWarning($"Failed to load image from: {screenshotPath}");
            }
        }

        // Обработка данных GPU readback и сохранение на диск (в фоне)
        private async Task ProcessReadbackAsync(AsyncGPUReadbackRequest op, int w, int h, Entity entity, string id,
            RenderTexture rt, TaskCompletionSource<string> tcs)
        {
            try
            {
                if (op.hasError)
                {
                    tcs.TrySetResult(string.Empty);
                    return;
                }

                NativeArray<byte> data = op.GetData<byte>();
                byte[] raw = data.ToArray();
                byte[] pngBytes = null;

                await Task.Run(() =>
                {
                    try
                    {
                        pngBytes = ImageConversion.EncodeArrayToPNG(raw,
                            UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
                            (uint)w, (uint)h);
                    }
                    catch
                    {
                        pngBytes = null;
                    }
                });

                if (pngBytes == null)
                {
                    tcs.TrySetResult(string.Empty);
                    return;
                }

                string fileName = $"entity_{entity.EntityId}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                string path = Path.Combine(SavesDirectoryPath, fileName);
                await File.WriteAllBytesAsync(path, pngBytes);
                _idToPath[id] = path;
                tcs.TrySetResult(id);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Async capture failed: {e.Message}");
                tcs.TrySetResult(string.Empty);
            }
            finally
            {
                if (rt != null)
                {
                    rt.Release();
                }
            }
        }
    }
}

