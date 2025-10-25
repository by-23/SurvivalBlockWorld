using UnityEngine;

public abstract class Singleton<T> : MonoBehaviour where T : Component
{
    #region Fields

    /// <summary>
    ///     The instance.
    /// </summary>
    private static T instance;

    #endregion

    #region Properties

    /// <summary>
    ///     Gets the instance.
    /// </summary>
    /// <value>The instance.</value>
    public static T Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<T>();
#if UNITY_EDITOR
                // Если не играем в режиме редактора, не создаём автоматически
                if (!Application.isPlaying && instance == null)
                {
                    Debug.LogError($"Singleton<{typeof(T)}> instance не найден. Добавьте объект вручную на сцене.");
                    return null;
                }
#endif
                if (instance == null)
                {
                    var obj = new GameObject(typeof(T).Name);
                    instance = obj.AddComponent<T>();
                }
            }

            return instance;
        }
    }

    #endregion

    #region Methods

    /// <summary>
    ///     Use this for initialization.
    /// </summary>
    protected virtual void Awake()
    {
        if (instance == null)
            instance = this as T;
        // DontDestroyOnLoad ( gameObject );
        else
            Destroy(gameObject);
    }

    #endregion
}