using UnityEngine;

public abstract class Singleton<T> : MonoBehaviour where T : Component
{
    private static T instance;

    public static T Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<T>();
#if UNITY_EDITOR
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

    protected virtual void Awake()
    {
        if (instance == null)
            instance = this as T;
        else
            Destroy(gameObject);
    }
}