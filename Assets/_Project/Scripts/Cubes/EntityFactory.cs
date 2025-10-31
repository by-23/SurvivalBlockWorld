using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Фабрика для создания Entity объектов с единообразной настройкой компонентов.
/// Устраняет дублирование кода создания Entity в разных частях проекта.
/// </summary>
public static class EntityFactory
{
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
        GameObject entityObject = new GameObject(entityName ?? $"Entity_{System.DateTime.Now.Ticks}");
        entityObject.transform.position = position;
        entityObject.transform.rotation = rotation;
        entityObject.transform.localScale = scale;

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

        // Добавляем Rigidbody
        Rigidbody rb = entityObject.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = entityObject.AddComponent<Rigidbody>();
        }

        // Настраиваем Rigidbody
        if (cubeCount.HasValue)
        {
            rb.mass = cubeCount.Value / 10f;
        }
        else
        {
            rb.mass = 1f;
        }

        rb.drag = 0f;
        rb.angularDrag = 0.05f;
        rb.useGravity = true;
        rb.isKinematic = isKinematic;

        // Добавляем обязательные компоненты Entity
        if (entityObject.GetComponent<Entity>() == null)
        {
            entityObject.AddComponent<Entity>();
        }

        // Добавляем вспомогательные компоненты (если еще не добавлены)
        if (entityObject.GetComponent<EntityMeshCombiner>() == null)
        {
            entityObject.AddComponent<EntityMeshCombiner>();
        }

        if (entityObject.GetComponent<EntityHookManager>() == null)
        {
            entityObject.AddComponent<EntityHookManager>();
        }

        if (entityObject.GetComponent<EntityVehicleConnector>() == null)
        {
            entityObject.AddComponent<EntityVehicleConnector>();
        }
    }
}
