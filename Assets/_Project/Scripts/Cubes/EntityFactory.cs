using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Фабрика для создания Entity объектов с единообразной настройкой компонентов.
/// Устраняет дублирование кода создания Entity в разных частях проекта.
/// </summary>
public static class EntityFactory
{
    // Путь к префабу-пустышке в Resources (можно переопределить до первого вызова)
    public static string EntityPrefabPath = "EntityPrefab";

    // Кэш префаба, чтобы не вызывать Resources.Load каждый раз
    private static GameObject _cachedEntityPrefab;

    // Возвращает кэшированный префаб, если он есть в Resources; иначе null
    private static GameObject GetEntityPrefab()
    {
        if (_cachedEntityPrefab == null)
        {
            _cachedEntityPrefab = Resources.Load<GameObject>(EntityPrefabPath);
        }

        return _cachedEntityPrefab;
    }

    /// <summary>
    /// Создает новый Entity GameObject с базовыми компонентами и настройками.
    /// </summary>
    /// <param name="position">Позиция Entity в мировых координатах</param>
    /// <param name="rotation">Поворот Entity</param>
    /// <param name="scale">Масштаб Entity</param>
    /// <param name="isKinematic">Должен ли Rigidbody быть кинематическим</param>
    /// <param name="entityName">Имя GameObject (опционально, по умолчанию генерируется)</param>
    /// <returns>Созданный Entity компонент</returns>
    public static Entity CreateEntity(Vector3 position, Quaternion rotation, Vector3 scale, bool isKinematic = true,
        string entityName = null)
    {
        // Сначала пытаемся создать из префаба-пустышки, чтобы избежать рантайм-добавления компонентов
        GameObject prefab = GetEntityPrefab();
        GameObject entityObject;
        if (prefab != null)
        {
            entityObject = Object.Instantiate(prefab);
            if (!string.IsNullOrEmpty(entityName)) entityObject.name = entityName; // сохраняем заданное имя
        }
        else
        {
            // Фоллбэк: создаем пустой объект, как раньше
            entityObject = new GameObject(entityName ?? $"Entity_{System.DateTime.Now.Ticks}");
        }

        // Приводим трансформ к заданным параметрам
        Transform t = entityObject.transform;
        t.position = position;
        t.rotation = rotation;
        t.localScale = scale;

        SetupEntityComponents(entityObject, isKinematic);

        return entityObject.GetComponent<Entity>();
    }

    /// <summary>
    /// Создает Entity из списка кубов. Автоматически вычисляет центр группы для позиции Entity.
    /// </summary>
    /// <param name="cubes">Список кубов для создания Entity</param>
    /// <param name="centerPosition">Центр группы (опционально, вычисляется автоматически если null)</param>
    /// <param name="isKinematic">Должен ли Rigidbody быть кинематическим</param>
    /// <returns>Созданный Entity компонент или null если нет кубов</returns>
    public static Entity CreateEntityFromCubes(List<Cube> cubes, Vector3? centerPosition = null,
        bool isKinematic = true)
    {
        if (cubes == null || cubes.Count == 0)
            return null;

        // Удаляем null кубы
        cubes = cubes.Where(c => c != null).ToList();
        if (cubes.Count == 0)
            return null;

        // Вычисляем центр группы для позиции Entity
        Vector3 center = centerPosition ?? Vector3.zero;
        if (!centerPosition.HasValue)
        {
            foreach (var cube in cubes)
            {
                center += cube.transform.position;
            }

            center /= cubes.Count;
        }

        // Создаем Entity
        Entity entity = CreateEntity(center, Quaternion.identity, Vector3.one, isKinematic);

        // Прикрепляем кубы к Entity
        EntityCubeAttacher.AttachCubesToEntity(cubes, entity, updateEntity: true);

        // Инициализируем Entity
        entity.StartSetup();

        return entity;
    }

    /// <summary>
    /// Настраивает все необходимые компоненты для Entity GameObject.
    /// </summary>
    /// <param name="entityObject">GameObject для настройки</param>
    /// <param name="isKinematic">Должен ли Rigidbody быть кинематическим</param>
    /// <param name="cubeCount">Количество кубов для расчета массы (опционально)</param>
    public static void SetupEntityComponents(GameObject entityObject, bool isKinematic = true, int? cubeCount = null)
    {
        if (entityObject == null)
        {
            Debug.LogError("EntityFactory.SetupEntityComponents: entityObject is null!");
            return;
        }

        // Получаем/добавляем Rigidbody (на префабе он уже должен быть)
        Rigidbody rb;
        if (!entityObject.TryGetComponent<Rigidbody>(out rb))
            rb = entityObject.AddComponent<Rigidbody>();

        // Настраиваем Rigidbody — только если значения реально отличаются
        float desiredMass = cubeCount.HasValue ? cubeCount.Value * 0.1f : 1f;
        if (!Mathf.Approximately(rb.mass, desiredMass)) rb.mass = desiredMass;
        if (!Mathf.Approximately(rb.drag, 0f)) rb.drag = 0f;
        if (!Mathf.Approximately(rb.angularDrag, 0.05f)) rb.angularDrag = 0.05f;
        if (!rb.useGravity) rb.useGravity = true;
        // Обязательные компоненты — на префабе уже есть; добавляем только в фоллбэке
        if (!entityObject.TryGetComponent<Entity>(out var entity))
            entity = entityObject.AddComponent<Entity>();

        // Вспомогательные компоненты — аналогично
        if (!entityObject.TryGetComponent<EntityMeshCombiner>(out _))
            entityObject.AddComponent<EntityMeshCombiner>();

        if (!entityObject.TryGetComponent<EntityHookManager>(out _))
            entityObject.AddComponent<EntityHookManager>();

        entity.SetKinematicState(isKinematic, true);
    }
}
