using UnityEngine;

/// <summary>
/// Утилита для настройки кубов после создания.
/// Устраняет дублирование кода настройки цвета и типа кубов.
/// </summary>
public static class CubeSetupHelper
{
    /// <summary>
    /// Настраивает цвет куба. Поддерживает ColorCube компонент или MaterialPropertyBlock.
    /// </summary>
    /// <param name="cube">GameObject куба для настройки</param>
    /// <param name="color">Цвет для установки</param>
    public static void SetupCubeColor(GameObject cube, Color color)
    {
        if (cube == null)
        {
            Debug.LogWarning("CubeSetupHelper.SetupCubeColor: cube is null!");
            return;
        }

        // Пробуем использовать ColorCube компонент
        ColorCube colorCube = cube.GetComponent<ColorCube>();
        if (colorCube != null)
        {
            colorCube.Setup(color);
            return;
        }

        // Если ColorCube нет, используем MaterialPropertyBlock
        MeshRenderer cubeRenderer = cube.GetComponent<MeshRenderer>();
        if (cubeRenderer != null)
        {
            MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
            propertyBlock.SetColor("_BaseColor", color);
            cubeRenderer.SetPropertyBlock(propertyBlock);
        }
    }

    /// <summary>
    /// Устанавливает тип блока (BlockTypeID) для куба.
    /// </summary>
    /// <param name="cube">GameObject куба для настройки</param>
    /// <param name="blockTypeId">ID типа блока</param>
    public static void SetupCubeType(GameObject cube, byte blockTypeId)
    {
        if (cube == null)
        {
            Debug.LogWarning("CubeSetupHelper.SetupCubeType: cube is null!");
            return;
        }

        Cube cubeComponent = cube.GetComponent<Cube>();
        if (cubeComponent != null)
        {
            cubeComponent.BlockTypeID = blockTypeId;
        }
        else
        {
            Debug.LogWarning($"CubeSetupHelper.SetupCubeType: GameObject {cube.name} doesn't have Cube component!");
        }
    }

    /// <summary>
    /// Настраивает и цвет, и тип куба за один вызов.
    /// </summary>
    /// <param name="cube">GameObject куба для настройки</param>
    /// <param name="color">Цвет для установки</param>
    /// <param name="blockTypeId">ID типа блока</param>
    public static void SetupCube(GameObject cube, Color color, byte blockTypeId)
    {
        SetupCubeColor(cube, color);
        SetupCubeType(cube, blockTypeId);
    }
}
