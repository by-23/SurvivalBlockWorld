using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Утилита для прикрепления кубов к Entity с правильной конвертацией позиций.
/// Устраняет дублирование кода прикрепления кубов в разных частях проекта.
/// </summary>
public static class EntityCubeAttacher
{
    /// <summary>
    /// Прикрепляет один куб к Entity с конвертацией мировой позиции в локальную.
    /// </summary>
    /// <param name="cube">Куб для прикрепления</param>
    /// <param name="entity">Entity, к которому прикрепляется куб</param>
    /// <param name="preserveWorldPosition">Сохранять ли мировую позицию при установке родителя</param>
    public static void AttachCubeToEntity(Cube cube, Entity entity, bool preserveWorldPosition = true)
    {
        if (cube == null || entity == null)
        {
            Debug.LogWarning("EntityCubeAttacher.AttachCubeToEntity: cube or entity is null!");
            return;
        }

        if (cube.transform.parent == entity.transform)
        {
            // Куб уже прикреплен к этому Entity
            return;
        }

        Transform entityTransform = entity.transform;
        Vector3 entityWorldPos = entityTransform.position;
        float entityScale = entityTransform.localScale.x;

        // Сохраняем мировую позицию и поворот
        Vector3 worldPos = cube.transform.position;
        Quaternion worldRot = cube.transform.rotation;

        // Устанавливаем родителя
        if (preserveWorldPosition)
        {
            cube.transform.SetParent(entityTransform, true);
        }
        else
        {
            cube.transform.SetParent(entityTransform);
            // Конвертируем мировую позицию в локальную относительно Entity
            cube.transform.localPosition = (worldPos - entityWorldPos) / entityScale;
            cube.transform.localRotation = worldRot;
        }

        // Устанавливаем связь с Entity
        cube.SetEntity(entity);
    }

    /// <summary>
    /// Прикрепляет список кубов к Entity с конвертацией позиций.
    /// </summary>
    /// <param name="cubes">Список кубов для прикрепления</param>
    /// <param name="entity">Entity, к которому прикрепляются кубы</param>
    /// <param name="updateEntity">Обновить ли Entity после прикрепления (UpdateMassAndCubes и StartSetup)</param>
    public static void AttachCubesToEntity(List<Cube> cubes, Entity entity, bool updateEntity = true)
    {
        if (cubes == null || cubes.Count == 0 || entity == null)
        {
            Debug.LogWarning("EntityCubeAttacher.AttachCubesToEntity: invalid input parameters!");
            return;
        }

        // Удаляем null кубы
        cubes.RemoveAll(c => c == null);
        if (cubes.Count == 0)
            return;

        Transform entityTransform = entity.transform;
        Vector3 entityWorldPos = entityTransform.position;
        float entityScale = entityTransform.localScale.x;

        // Прикрепляем каждый куб
        foreach (var cube in cubes)
        {
            if (cube == null || cube.transform.parent != null)
                continue;

            // Сохраняем мировую позицию и поворот
            Vector3 worldPos = cube.transform.position;
            Quaternion worldRot = cube.transform.rotation;

            // Устанавливаем родителя
            cube.transform.SetParent(entityTransform);

            // Конвертируем мировую позицию в локальную относительно Entity
            cube.transform.localPosition = (worldPos - entityWorldPos) / entityScale;
            cube.transform.localRotation = worldRot;

            // Устанавливаем связь с Entity
            cube.SetEntity(entity);
        }

        // Обновляем Entity если требуется
        if (updateEntity)
        {
            entity.UpdateMassAndCubes();
            entity.StartSetup();
        }
    }
}
