using UnityEngine;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;

namespace Assets._Project.Scripts.UI
{
    /// <summary>
    /// Утилитный класс для общей логики загрузки сцен и мира
    /// </summary>
    public static class SceneLoadHelper
    {
        /// <summary>
        /// Асинхронно загружает сцену по индексу и ждет полной загрузки
        /// </summary>
        /// <param name="sceneIndex">Индекс сцены в Build Settings</param>
        /// <param name="onProgress">Callback для отслеживания прогресса загрузки</param>
        public static async Task LoadSceneAsync(int sceneIndex, System.Action<float> onProgress = null)
        {
            // Проверяем валидность индекса
            if (sceneIndex < 0 || sceneIndex >= SceneManager.sceneCountInBuildSettings)
            {
                Debug.LogError(
                    $"Неверный индекс сцены: {sceneIndex}. Доступно сцен: {SceneManager.sceneCountInBuildSettings}");
                return;
            }

            Debug.Log($"Начинаем загрузку сцены по индексу: {sceneIndex}");

            // Загружаем сцену асинхронно по индексу
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneIndex);

            if (asyncLoad == null)
            {
                Debug.LogError($"Не удалось начать загрузку сцены с индексом {sceneIndex}");
                return;
            }

            // Ждем завершения загрузки
            while (!asyncLoad.isDone)
            {
                float progress = asyncLoad.progress;
                Debug.Log($"Прогресс загрузки сцены: {progress * 100:F1}%");

                // Вызываем callback для обновления UI прогресса
                onProgress?.Invoke(progress);

                // Ждем один кадр
                await Task.Yield();
            }

            Debug.Log($"Сцена с индексом {sceneIndex} успешно загружена");
        }

        /// <summary>
        /// Загружает мир асинхронно с обработкой ошибок
        /// </summary>
        /// <param name="mapName">Имя карты для загрузки</param>
        /// <param name="onProgress">Callback для отслеживания прогресса загрузки мира</param>
        /// <returns>True если загрузка успешна, false если произошла ошибка</returns>
        public static async Task<bool> LoadWorldAsync(string mapName, System.Action<float> onProgress = null)
        {
            if (SaveSystem.Instance == null)
            {
                Debug.LogError("SaveSystem.Instance is null");
                return false;
            }

            bool loadSuccess = await SaveSystem.Instance.LoadWorldAsync(mapName, onProgress);

            if (loadSuccess)
            {
                Debug.Log($"Мир '{mapName}' успешно загружен");
                
                if (Player.Instance != null)
                {
                    Player.Instance.ForcePlayerControlMode();
                }
            }
            else
            {
                Debug.LogError($"Ошибка загрузки мира '{mapName}'");
            }

            return loadSuccess;
        }

        /// <summary>
        /// Полная загрузка сцены и мира
        /// </summary>
        /// <param name="sceneIndex">Индекс сцены для загрузки</param>
        /// <param name="mapName">Имя карты для загрузки</param>
        /// <param name="onSceneProgress">Callback для прогресса загрузки сцены</param>
        /// <param name="onWorldProgress">Callback для прогресса загрузки мира</param>
        /// <returns>True если загрузка успешна, false если произошла ошибка</returns>
        public static async Task<bool> LoadSceneAndWorldAsync(int sceneIndex, string mapName,
            System.Action<float> onSceneProgress = null,
            System.Action<float> onWorldProgress = null)
        {
            // Сначала загружаем сцену
            await LoadSceneAsync(sceneIndex, onSceneProgress);

            // Затем загружаем мир
            return await LoadWorldAsync(mapName, onWorldProgress);
        }
    }
}
