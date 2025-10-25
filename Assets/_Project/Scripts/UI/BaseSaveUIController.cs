using UnityEngine;

namespace Assets._Project.Scripts.UI
{
    /// <summary>
    /// Базовый класс для UI контроллеров, работающих с SaveListManager
    /// </summary>
    public abstract class BaseSaveUIController : MonoBehaviour
    {
        [Header("Save List Manager")] [SerializeField]
        protected SaveListManager SaveListManager;

        protected virtual void Start()
        {
            SubscribeToSaveListEvents();
        }

        protected virtual void OnDestroy()
        {
            UnsubscribeFromSaveListEvents();
        }

        /// <summary>
        /// Подписывается на события SaveListManager
        /// </summary>
        protected virtual void SubscribeToSaveListEvents()
        {
            if (SaveListManager != null)
            {
                SaveListManager.OnMapLoadRequested += OnMapLoadRequested;
                SaveListManager.OnMapDeleteRequested += OnMapDeleteRequested;
                SaveListManager.OnLoadingStarted += OnLoadingStarted;
                SaveListManager.OnLoadingCompleted += OnLoadingCompleted;
            }
        }

        /// <summary>
        /// Отписывается от событий SaveListManager
        /// </summary>
        protected virtual void UnsubscribeFromSaveListEvents()
        {
            if (SaveListManager != null)
            {
                SaveListManager.OnMapLoadRequested -= OnMapLoadRequested;
                SaveListManager.OnMapDeleteRequested -= OnMapDeleteRequested;
                SaveListManager.OnLoadingStarted -= OnLoadingStarted;
                SaveListManager.OnLoadingCompleted -= OnLoadingCompleted;
            }
        }

        /// <summary>
        /// Обработчик запроса загрузки карты
        /// </summary>
        /// <param name="mapName">Имя карты</param>
        protected abstract void OnMapLoadRequested(string mapName);

        /// <summary>
        /// Обработчик запроса удаления карты
        /// </summary>
        /// <param name="mapName">Имя карты</param>
        protected virtual void OnMapDeleteRequested(string mapName)
        {
            Debug.Log($"Удаление карты: {mapName}");
            if (SaveListManager != null)
            {
                SaveListManager.DeleteMap(mapName);
            }
        }

        /// <summary>
        /// Обработчик начала загрузки
        /// </summary>
        protected virtual void OnLoadingStarted()
        {
            // Переопределить в наследниках при необходимости
        }

        /// <summary>
        /// Обработчик завершения загрузки
        /// </summary>
        protected virtual void OnLoadingCompleted()
        {
            // Переопределить в наследниках при необходимости
        }
    }
}
