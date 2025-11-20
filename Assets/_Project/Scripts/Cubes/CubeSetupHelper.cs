using UnityEngine;

public static class CubeSetupHelper
{
    public static void SetupCubeColor(GameObject cube, Color color)
    {
        if (cube == null)
        {
            Debug.LogWarning("CubeSetupHelper.SetupCubeColor: cube is null!");
            return;
        }

        ColorCube colorCube = cube.GetComponent<ColorCube>();
        if (colorCube != null)
        {
            colorCube.Setup(color);
            return;
        }

        MeshRenderer cubeRenderer = cube.GetComponent<MeshRenderer>();
        if (cubeRenderer != null)
        {
            MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
            propertyBlock.SetColor("_BaseColor", color);
            cubeRenderer.SetPropertyBlock(propertyBlock);
        }
    }

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

    public static void SetupCube(GameObject cube, Color color, byte blockTypeId)
    {
        SetupCubeColor(cube, color);
        SetupCubeType(cube, blockTypeId);
    }
}
